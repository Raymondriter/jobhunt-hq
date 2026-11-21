using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

/// <summary>
/// The pipeline board (M4): the signed-in user's applications grouped into
/// columns by status, in pipeline order. Read-only — cards link to Details.
/// </summary>
[Authorize]
public class BoardController : Controller
{
    private readonly ApplicationDbContext _context;

    public BoardController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Pipeline order: the happy path first, then the closed outcomes.
    private static readonly ApplicationStatus[] PipelineOrder =
    {
        ApplicationStatus.Saved,
        ApplicationStatus.Applied,
        ApplicationStatus.Screening,
        ApplicationStatus.Interview,
        ApplicationStatus.Offer,
        ApplicationStatus.Rejected,
        ApplicationStatus.Ghosted,
        ApplicationStatus.Withdrawn
    };

    // GET: Board
    public async Task<IActionResult> Index()
    {
        var applications = await _context.JobApplications
            .Where(a => a.UserId == CurrentUserId)
            .OrderByDescending(a => a.UpdatedUtc)
            .ToListAsync();

        var model = new BoardViewModel
        {
            Columns = PipelineOrder
                .Select(status => new BoardColumn
                {
                    Status = status,
                    Applications = applications.Where(a => a.Status == status).ToList()
                })
                .ToList(),
            Today = DateOnly.FromDateTime(DateTime.Today)
        };
        return View(model);
    }
}
