using System.Security.Claims;
using JobHuntHQ.Controllers;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHuntHQ.Tests;

public class DashboardFollowUpTests
{
    [Fact]
    public async Task Dashboard_keeps_overdue_and_week_ahead_follow_ups_and_hides_closed_ones()
    {
        var (db, connection) = SqliteAppDb.Create();
        await using var _ = connection;
        await using var context = db;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var mine = "user-a";
        await AddAsync(context, mine, "Overdue", ApplicationStatus.Applied, today.AddDays(-3));
        await AddAsync(context, mine, "Today", ApplicationStatus.Saved, today);
        await AddAsync(context, mine, "Day seven", ApplicationStatus.Interview, today.AddDays(7));
        await AddAsync(context, mine, "Day eight", ApplicationStatus.Applied, today.AddDays(8));
        await AddAsync(context, mine, "Undated", ApplicationStatus.Applied, null);
        await AddAsync(context, mine, "Rejected", ApplicationStatus.Rejected, today.AddDays(-1));
        await AddAsync(context, mine, "Ghosted", ApplicationStatus.Ghosted, today);
        await AddAsync(context, mine, "Withdrawn", ApplicationStatus.Withdrawn, today.AddDays(1));
        await AddAsync(context, "user-b", "Other account", ApplicationStatus.Applied, today.AddDays(-1));

        var controller = new HomeController(NullLogger<HomeController>.Instance, context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = User(mine) }
            }
        };

        var result = await controller.Dashboard();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal(
            new[] { "Overdue", "Today", "Day seven" },
            model.DueSoon.Select(a => a.Company).ToArray());
        Assert.Equal(today, model.Today);
        Assert.DoesNotContain(model.DueSoon, a => a.UserId != mine);
        Assert.Equal(8, model.TotalApplications);
        Assert.Equal(5, model.ActiveApplicationsCount);
    }

    [Fact]
    public void Index_sends_a_signed_in_user_to_the_dashboard_and_keeps_anonymous_visitors_on_the_landing_page()
    {
        var (db, connection) = SqliteAppDb.Create();
        using var _ = connection;
        using var context = db;
        var controller = new HomeController(NullLogger<HomeController>.Instance, context);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = User("user-a") }
        };
        var signedIn = Assert.IsType<RedirectToActionResult>(controller.Index());
        Assert.Equal(nameof(HomeController.Dashboard), signedIn.ActionName);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        Assert.IsType<ViewResult>(controller.Index());
    }

    private static ClaimsPrincipal User(string userId) =>
        new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
            authenticationType: "test"));

    private static async Task AddAsync(
        JobHuntHQ.Data.ApplicationDbContext context,
        string userId,
        string company,
        ApplicationStatus status,
        DateOnly? due)
    {
        var now = DateTime.UtcNow;
        context.JobApplications.Add(new JobApplication
        {
            UserId = userId,
            Company = company,
            RoleTitle = "Engineer",
            Status = status,
            NextActionDue = due,
            DateSaved = new DateOnly(2026, 11, 1),
            CreatedUtc = now,
            UpdatedUtc = now
        });
        await context.SaveChangesAsync();
    }
}
