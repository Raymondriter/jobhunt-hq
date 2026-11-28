using System.Text;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Tests;

public class CalendarControllerTests
{
    private const string OtherKey = "jhq_otheruserkey12345";

    [Fact]
    public async Task Feed_ReturnsNotFoundWithoutAnotherUsersBody()
    {
        await Reject(string.Empty);
        await Reject("   ");
        await Reject("jhq_short");
        await Reject("not-a-jhq-key-but-long");
        await Reject("jhq_unknownhashkey01");
        await Reject("jhq_revokedtoken0001", (db, key) =>
            db.ApiTokens.Add(Token("user-a", key, ApiToken.CalendarScope, revoked: true)));
        await Reject("jhq_appsreadtoken001", (db, key) =>
            db.ApiTokens.Add(Token("user-a", key, "applications:read", revoked: false)));
    }

    [Fact]
    public async Task Feed_ReturnsOwnerCalendarAndRecordsUse()
    {
        const string raw = "jhq_livecalendartoken";
        await TestApp.UsingDb(async db =>
        {
            var owner = TestApp.App("user-a", "OwnerInterviewCo", ApplicationStatus.Interview);
            owner.InterviewRounds.Add(new InterviewRound
            {
                UserId = "user-a",
                RoundNumber = 1,
                RoundType = "Technical",
                ScheduledLocal = new DateTime(2026, 11, 27, 9, 0, 0),
                DurationMinutes = 30,
                TimeZoneLabel = "PT",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            var other = TestApp.App("user-b", "OtherUserCo", ApplicationStatus.Applied, DateOnly.FromDateTime(DateTime.Today));
            other.NextAction = "OtherUserFollowUpSecret";
            other.NextActionDue = DateOnly.FromDateTime(DateTime.Today).AddDays(2);
            var token = Token("user-a", raw, ApiToken.CalendarScope, revoked: false);
            db.AddRange(owner, other, token);
            await db.SaveChangesAsync();
            Assert.Null(token.LastUsedUtc);

            var controller = TestApp.Calendar(db);
            var result = await controller.Feed(raw);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.StartsWith("text/calendar", file.ContentType, StringComparison.OrdinalIgnoreCase);
            var body = Encoding.UTF8.GetString(file.FileContents);
            Assert.Contains("OwnerInterviewCo", body, StringComparison.Ordinal);
            Assert.DoesNotContain("OtherUserCo", body, StringComparison.Ordinal);
            Assert.DoesNotContain("OtherUserFollowUpSecret", body, StringComparison.Ordinal);
            Assert.Contains("private", controller.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);

            await db.Entry(token).ReloadAsync();
            Assert.NotNull(token.LastUsedUtc);
        });
    }

    private static async Task Reject(string key, Action<ApplicationDbContext, string>? seedMatching = null)
    {
        await TestApp.UsingDb(async db =>
        {
            var other = TestApp.App("user-b", "OtherUserCo", ApplicationStatus.Applied, DateOnly.FromDateTime(DateTime.Today));
            other.NextAction = "OtherUserFollowUpSecret";
            other.NextActionDue = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
            db.JobApplications.Add(other);
            db.ApiTokens.Add(Token("user-b", OtherKey, ApiToken.CalendarScope, revoked: false));
            seedMatching?.Invoke(db, key);
            await db.SaveChangesAsync();

            var controller = TestApp.Calendar(db);
            var result = await controller.Feed(key);
            Assert.IsType<NotFoundResult>(result);
        });
    }

    private static ApiToken Token(string userId, string raw, string scopes, bool revoked) =>
        new()
        {
            UserId = userId,
            Name = "Calendar",
            TokenPrefix = raw.Length >= 12 ? raw[..12] : raw,
            TokenHash = ApiToken.ComputeSha256Hex(raw),
            Scopes = scopes,
            CreatedUtc = DateTime.UtcNow,
            RevokedUtc = revoked ? DateTime.UtcNow : null
        };
}
