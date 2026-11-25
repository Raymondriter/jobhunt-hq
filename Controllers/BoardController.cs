using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

/// <summary>
/// The pipeline board (M4): the signed-in user's applications grouped into
/// columns by status, in pipeline order. Cards link to Details, and since
/// phase 3 they can be moved between columns — by drag-and-drop or by the
/// status select on each card — through <see cref="Move"/>.
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
                    Applications = applications.Where(a => a.Status == status).ToList(),
                    WipSoftLimit = WipSoftLimits.GetValueOrDefault(status),
                    WipHint = WipHints.GetValueOrDefault(status)
                })
                .ToList(),
            Today = DateOnly.FromDateTime(DateTime.Today)
        };
        return View(model);
    }

    // Soft WIP nudges (phase 3): a gentle note on the column header when a
    // column grows past these counts. Display only — Move never refuses.
    private static readonly IReadOnlyDictionary<ApplicationStatus, int> WipSoftLimits =
        new Dictionary<ApplicationStatus, int>
        {
            [ApplicationStatus.Saved] = 10,
            [ApplicationStatus.Applied] = 15
        };

    private static readonly IReadOnlyDictionary<ApplicationStatus, string> WipHints =
        new Dictionary<ApplicationStatus, string>
        {
            [ApplicationStatus.Saved] = "consider a cleanup or a follow-up sweep",
            [ApplicationStatus.Applied] = "consider a cleanup or a follow-up sweep"
        };

    // POST: Board/Move — move one of the signed-in user's applications to a
    // new status. Used by drag-and-drop and by the per-card status select.
    //
    // Ownership: the lookup filters on the current user's id, so an id that
    // belongs to someone else is indistinguishable from one that doesn't
    // exist (404 either way). Antiforgery: [ValidateAntiForgeryToken] plus
    // the token the Board page renders; board.js posts it back as the
    // __RequestVerificationToken form field.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, ApplicationStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            return BadRequest(new { ok = false, error = "Unknown status." });
        }

        var application = await _context.JobApplications
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
        if (application is null)
        {
            return NotFound(new { ok = false, error = "Application not found." });
        }

        if (application.Status != status)
        {
            application.Status = status;
            if (status == ApplicationStatus.Applied && !application.DateApplied.HasValue)
            {
                application.DateApplied = DateOnly.FromDateTime(DateTime.Today);
            }
            application.UpdatedUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Json(new
        {
            ok = true,
            status = status.ToString(),
            dateApplied = application.DateApplied?.ToString("yyyy-MM-dd")
        });
    }
}
