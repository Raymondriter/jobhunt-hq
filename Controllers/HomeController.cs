using System.Diagnostics;
using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction(nameof(Dashboard));

        return View();
    }

    [Authorize]
    [HttpGet("/dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var mine = _context.JobApplications.Where(a => a.UserId == userId);

        var counts = await mine
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var statusCounts = Enum.GetValues<ApplicationStatus>()
            .ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekOut = today.AddDays(7);

        var dueSoon = await mine
            .Where(a => a.NextActionDue != null
                        && a.NextActionDue <= weekOut
                        && a.Status != ApplicationStatus.Rejected
                        && a.Status != ApplicationStatus.Ghosted
                        && a.Status != ApplicationStatus.Withdrawn)
            .OrderBy(a => a.NextActionDue)
            .ThenBy(a => a.Id)
            .ToListAsync();

        var recent = await mine
            .Include(a => a.ResumeVersion)
            .OrderByDescending(a => a.UpdatedUtc)
            .Take(5)
            .ToListAsync();

        var upcomingInterviews = await _context.InterviewRounds
            .Include(r => r.JobApplication)
            .Where(r => r.UserId == userId && !r.Completed)
            .OrderBy(r => r.ScheduledLocal == null)
            .ThenBy(r => r.ScheduledLocal)
            .ThenBy(r => r.RoundNumber)
            .ToListAsync();

        var activeInterviewApps = await mine
            .Include(a => a.InterviewRounds)
            .Where(a => a.Status == ApplicationStatus.Screening || a.Status == ApplicationStatus.Interview)
            .OrderByDescending(a => a.UpdatedUtc)
            .ToListAsync();

        var hasProfile = await _context.SearchProfiles
            .AnyAsync(p => p.UserId == userId);
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == userId)
            .Select(r => new { r.Id, HasFile = r.BlobName != null })
            .ToListAsync();

        var activeCount = statusCounts
            .Where(kv => !kv.Key.IsClosed())
            .Sum(kv => kv.Value);
        var appliedCount = statusCounts
            .Where(kv => kv.Key != ApplicationStatus.Saved && kv.Key != ApplicationStatus.Withdrawn)
            .Sum(kv => kv.Value);
        var respondedCount =
            statusCounts[ApplicationStatus.Screening]
            + statusCounts[ApplicationStatus.Interview]
            + statusCounts[ApplicationStatus.Offer]
            + statusCounts[ApplicationStatus.Rejected];

        var model = new DashboardViewModel
        {
            StatusCounts = statusCounts,
            TotalApplications = statusCounts.Values.Sum(),
            ActiveApplicationsCount = activeCount,
            AppliedCount = appliedCount,
            RespondedCount = respondedCount,
            HasSearchProfile = hasProfile,
            HasResumeVersion = resumes.Count > 0,
            HasUploadedResumePdf = resumes.Any(r => r.HasFile),
            DueSoon = dueSoon,
            UpcomingInterviews = upcomingInterviews,
            ActiveInterviewApplications = activeInterviewApps,
            Today = today,
            RecentlyUpdated = recent
        };
        return View("Dashboard", model);
    }

    public IActionResult About()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
