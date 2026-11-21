using System.Diagnostics;
using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
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

    public async Task<IActionResult> Index()
    {
        // Anonymous visitors get the landing page; signed-in users get their dashboard.
        if (User.Identity?.IsAuthenticated != true)
        {
            return View();
        }

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

        var model = new DashboardViewModel
        {
            StatusCounts = statusCounts,
            TotalApplications = statusCounts.Values.Sum(),
            DueSoon = dueSoon,
            Today = today,
            RecentlyUpdated = recent
        };
        return View("Dashboard", model);
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
