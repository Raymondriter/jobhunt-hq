using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Mvc;

namespace JobHuntHQ.Tests;

public class QuietApplicationsTests
{
    [Fact]
    public async Task Dashboard_IncludesOnlyStaleAppliedRowsForTheSignedInUser()
    {
        await TestApp.UsingDb(async db =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var included = TestApp.App(TestApp.UserA, "SevenDayCo", ApplicationStatus.Applied, today.AddDays(-7));
            var sixDays = TestApp.App(TestApp.UserA, "SixDayCo", ApplicationStatus.Applied, today.AddDays(-6));
            var hasDue = TestApp.App(TestApp.UserA, "DueSetCo", ApplicationStatus.Applied, today.AddDays(-8));
            hasDue.NextActionDue = today;
            var noDate = TestApp.App(TestApp.UserA, "NoDateCo", ApplicationStatus.Applied);
            noDate.DateApplied = null;
            noDate.UpdatedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var screening = TestApp.App(TestApp.UserA, "ScreenCo", ApplicationStatus.Screening, today.AddDays(-30));
            var interview = TestApp.App(TestApp.UserA, "InterviewCo", ApplicationStatus.Interview, today.AddDays(-30));
            var otherUser = TestApp.App(TestApp.UserB, "OtherQuietCo", ApplicationStatus.Applied, today.AddDays(-7));

            db.AddRange(included, sixDays, hasDue, noDate, screening, interview, otherUser);
            await db.SaveChangesAsync();

            var model = await Dashboard(db, TestApp.UserA);

            Assert.Equal(1, model.QuietApplicationsTotalCount);
            var only = Assert.Single(model.QuietApplications);
            Assert.Equal(included.Id, only.Id);
            Assert.DoesNotContain(model.QuietApplications, app => app.Id == sixDays.Id);
            Assert.DoesNotContain(model.QuietApplications, app => app.Id == hasDue.Id);
            Assert.DoesNotContain(model.QuietApplications, app => app.Id == noDate.Id);
            Assert.DoesNotContain(model.QuietApplications, app => app.Id == screening.Id);
            Assert.DoesNotContain(model.QuietApplications, app => app.Id == interview.Id);
            Assert.DoesNotContain(model.QuietApplications, app => app.Id == otherUser.Id);
        });
    }

    [Fact]
    public async Task Dashboard_CapsQuietApplicationsAtEightOldest()
    {
        await TestApp.UsingDb(async db =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var rows = new List<JobApplication>();
            for (var i = 0; i < 9; i++)
            {
                rows.Add(TestApp.App(
                    TestApp.UserA,
                    "Quiet " + i,
                    ApplicationStatus.Applied,
                    today.AddDays(-30 + i)));
            }

            db.AddRange(rows);
            await db.SaveChangesAsync();
            Assert.Equal(rows.Select(row => row.Id).OrderBy(id => id), rows.Select(row => row.Id));

            var model = await Dashboard(db, TestApp.UserA);
            var oldestEight = rows
                .OrderBy(row => row.DateApplied)
                .ThenBy(row => row.Id)
                .Take(8)
                .Select(row => row.Id)
                .ToList();

            Assert.Equal(9, model.QuietApplicationsTotalCount);
            Assert.Equal(8, model.QuietApplications.Count);
            Assert.Equal(oldestEight, model.QuietApplications.Select(app => app.Id).ToList());
        });
    }

    private static async Task<DashboardViewModel> Dashboard(ApplicationDbContext db, string userId)
    {
        var result = await TestApp.Home(db, userId).Dashboard();
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<DashboardViewModel>(view.Model);
    }
}
