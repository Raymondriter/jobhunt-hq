using JobHuntHQ.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Tests;

public class BulkActionAndBookmarkletTests
{
    [Fact]
    public async Task BulkAction_RejectsEmptySelectionAndUnknownAction()
    {
        await TestApp.UsingDb(async db =>
        {
            var app = TestApp.App(TestApp.UserA, "KeepCo", ApplicationStatus.Saved);
            app.NextAction = "Leave me";
            app.NextActionDue = new DateOnly(2026, 1, 2);
            app.UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            db.Add(app);
            await db.SaveChangesAsync();
            var updated = app.UpdatedUtc;
            var controller = TestApp.Applications(db, TestApp.UserA);

            async Task AssertUnchanged()
            {
                await db.Entry(app).ReloadAsync();
                Assert.Equal(ApplicationStatus.Saved, app.Status);
                Assert.Equal("Leave me", app.NextAction);
                Assert.Equal(new DateOnly(2026, 1, 2), app.NextActionDue);
                Assert.Equal(updated, app.UpdatedUtc);
            }

            var missingIds = await controller.BulkAction(null, "applied");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(missingIds).ActionName);
            Assert.Equal(
                "Select at least one application and choose a bulk action.",
                controller.TempData["ErrorMessage"]);
            await AssertUnchanged();

            var emptyIds = await controller.BulkAction(Array.Empty<int>(), "applied");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(emptyIds).ActionName);
            await AssertUnchanged();

            var blankAction = await controller.BulkAction(new[] { app.Id }, "   ");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(blankAction).ActionName);
            await AssertUnchanged();

            var unknown = await controller.BulkAction(new[] { app.Id }, "delete");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(unknown).ActionName);
            Assert.Equal("Unknown bulk action.", controller.TempData["ErrorMessage"]);
            await AssertUnchanged();
        });
    }

    [Fact]
    public async Task BulkAction_DoesNotChangeAnotherUsersRows()
    {
        await TestApp.UsingDb(async db =>
        {
            var other = TestApp.App(TestApp.UserB, "OtherCo", ApplicationStatus.Screening);
            other.NextAction = "Their plan";
            other.NextActionDue = new DateOnly(2026, 2, 2);
            other.DateApplied = new DateOnly(2026, 1, 15);
            other.UpdatedUtc = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
            db.Add(other);
            await db.SaveChangesAsync();

            var controller = TestApp.Applications(db, TestApp.UserA);
            var result = await controller.BulkAction(new[] { other.Id }, "rejected");

            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
            await db.Entry(other).ReloadAsync();
            Assert.Equal(ApplicationStatus.Screening, other.Status);
            Assert.Equal("Their plan", other.NextAction);
            Assert.Equal(new DateOnly(2026, 2, 2), other.NextActionDue);
            Assert.Equal(new DateOnly(2026, 1, 15), other.DateApplied);
            Assert.Equal(new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc), other.UpdatedUtc);
        });
    }

    [Fact]
    public async Task BulkAction_AppliesFollowUpAppliedGhostedAndRejected()
    {
        await TestApp.UsingDb(async db =>
        {
            var keepAction = TestApp.App(TestApp.UserA, "KeepAction", ApplicationStatus.Applied);
            keepAction.NextAction = "Call them";
            var fillAction = TestApp.App(TestApp.UserA, "FillAction", ApplicationStatus.Applied);
            fillAction.NextAction = null;

            var keepDate = TestApp.App(TestApp.UserA, "KeepDate");
            keepDate.DateApplied = new DateOnly(2025, 8, 1);
            var fillDate = TestApp.App(TestApp.UserA, "FillDate");
            fillDate.DateApplied = null;

            var ghost = TestApp.App(TestApp.UserA, "GhostCo", ApplicationStatus.Applied);
            ghost.NextAction = "Ping";
            ghost.NextActionDue = new DateOnly(2026, 3, 1);
            var reject = TestApp.App(TestApp.UserA, "RejectCo", ApplicationStatus.Interview);
            reject.NextAction = "Prep";
            reject.NextActionDue = new DateOnly(2026, 3, 2);

            db.AddRange(keepAction, fillAction, keepDate, fillDate, ghost, reject);
            await db.SaveChangesAsync();
            var controller = TestApp.Applications(db, TestApp.UserA);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var follow = await controller.BulkAction(new[] { keepAction.Id, fillAction.Id }, "followup3");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(follow).ActionName);
            await db.Entry(keepAction).ReloadAsync();
            await db.Entry(fillAction).ReloadAsync();
            Assert.Equal("Call them", keepAction.NextAction);
            Assert.Equal("Send brief status follow-up", fillAction.NextAction);
            Assert.Equal(today.AddDays(3), keepAction.NextActionDue);
            Assert.Equal(today.AddDays(3), fillAction.NextActionDue);

            today = DateOnly.FromDateTime(DateTime.Today);
            var applied = await controller.BulkAction(new[] { keepDate.Id, fillDate.Id }, "applied");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(applied).ActionName);
            await db.Entry(keepDate).ReloadAsync();
            await db.Entry(fillDate).ReloadAsync();
            Assert.Equal(ApplicationStatus.Applied, keepDate.Status);
            Assert.Equal(ApplicationStatus.Applied, fillDate.Status);
            Assert.Equal(new DateOnly(2025, 8, 1), keepDate.DateApplied);
            Assert.Equal(today, fillDate.DateApplied);

            var ghosted = await controller.BulkAction(new[] { ghost.Id }, "ghosted");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(ghosted).ActionName);
            await db.Entry(ghost).ReloadAsync();
            Assert.Equal(ApplicationStatus.Ghosted, ghost.Status);
            Assert.Null(ghost.NextAction);
            Assert.Null(ghost.NextActionDue);

            var rejected = await controller.BulkAction(new[] { reject.Id }, "rejected");
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(rejected).ActionName);
            await db.Entry(reject).ReloadAsync();
            Assert.Equal(ApplicationStatus.Rejected, reject.Status);
            Assert.Null(reject.NextAction);
            Assert.Null(reject.NextActionDue);
        });
    }

    [Fact]
    public async Task AddPosting_ParsesBookmarkletTitleWithoutSaving()
    {
        await TestApp.UsingDb(async db =>
        {
            var controller = TestApp.Applications(db, TestApp.UserA);

            var linkedIn = Model(await controller.AddPosting(title: "Senior Engineer | LinkedIn jobs extra"));
            Assert.Equal("Senior Engineer", linkedIn.JobTitle);
            Assert.Equal(string.Empty, linkedIn.Company);
            Assert.NotNull(linkedIn.FetchNote);

            var split = Model(await controller.AddPosting(title: "Backend Engineer at Contoso Labs"));
            Assert.Equal("Backend Engineer", split.JobTitle);
            Assert.Equal("Contoso Labs", split.Company);
            Assert.NotNull(split.FetchNote);

            var kept = Model(await controller.AddPosting(company: "Contoso", title: "Backend Engineer at Contoso Labs"));
            Assert.Equal("Backend Engineer at Contoso Labs", kept.JobTitle);
            Assert.Equal("Contoso", kept.Company);

            var longText = new string('p', 12001);
            var urlPrefix = "https://jobs.example/";
            var longUrl = urlPrefix + new string('u', 1001 - urlPrefix.Length);
            var truncated = Model(await controller.AddPosting(url: longUrl, text: longText));
            Assert.Equal(12000, truncated.PostingText!.Length);
            Assert.Equal(longText[..12000], truncated.PostingText);
            Assert.Equal(1000, truncated.PostingUrl!.Length);
            Assert.Equal(longUrl[..1000], truncated.PostingUrl);
            Assert.NotNull(truncated.FetchNote);

            Assert.NotNull(Model(await controller.AddPosting(url: "https://jobs.example/only")).FetchNote);
            Assert.NotNull(Model(await controller.AddPosting(text: "A posting")).FetchNote);
            Assert.Null(Model(await controller.AddPosting()).FetchNote);

            Assert.Equal(0, await db.JobApplications.CountAsync());
        });
    }

    private static AddPostingViewModel Model(IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<AddPostingViewModel>(view.Model);
    }
}
