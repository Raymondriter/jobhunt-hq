using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

/// <summary>
/// Top-level Interviews Command Center (<c>/Interviews</c>): shows upcoming
/// and completed interview rounds across all applications, active interview-stage
/// applications, and manages the user's reusable STAR Story Bank.
/// </summary>
[Authorize]
public class InterviewsController : Controller
{
    private readonly ApplicationDbContext _context;

    public InterviewsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET: Interviews
    public async Task<IActionResult> Index(int? editStoryId = null)
    {
        var allRounds = await _context.InterviewRounds
            .Include(r => r.JobApplication)
            .Where(r => r.UserId == CurrentUserId)
            .ToListAsync();

        var upcoming = allRounds
            .Where(r => !r.Completed)
            .OrderBy(r => r.ScheduledLocal == null)
            .ThenBy(r => r.ScheduledLocal)
            .ThenBy(r => r.RoundNumber)
            .ToList();

        var completed = allRounds
            .Where(r => r.Completed)
            .OrderByDescending(r => r.ScheduledLocal ?? r.UpdatedUtc)
            .ToList();

        var activeApps = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == CurrentUserId
                        && (a.Status == ApplicationStatus.Screening
                            || a.Status == ApplicationStatus.Interview
                            || a.Status == ApplicationStatus.Offer))
            .OrderByDescending(a => a.UpdatedUtc)
            .ToListAsync();

        var stories = await _context.StoryBankItems
            .Where(s => s.UserId == CurrentUserId)
            .OrderByDescending(s => s.UpdatedUtc)
            .ToListAsync();

        StoryBankItem? editingStory = null;
        if (editStoryId.HasValue)
        {
            editingStory = stories.FirstOrDefault(s => s.Id == editStoryId.Value);
        }

        var model = new InterviewsHubViewModel
        {
            UpcomingRounds = upcoming,
            CompletedRounds = completed,
            ActiveInterviewApplications = activeApps,
            Stories = stories,
            EditingStory = editingStory,
            Today = DateOnly.FromDateTime(DateTime.Today)
        };

        return View(model);
    }

    // POST: Interviews/SaveStory — create or update a reusable STAR story.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStory(
        int? id,
        string title,
        string? tags,
        string? situation,
        string? task,
        string? action,
        string? result,
        string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["ErrorMessage"] = "Give your STAR story a headline title.";
            return RedirectAfterStory(returnUrl);
        }

        StoryBankItem story;
        if (id is int existingId && existingId > 0)
        {
            var found = await _context.StoryBankItems
                .FirstOrDefaultAsync(s => s.Id == existingId && s.UserId == CurrentUserId);
            if (found is null)
            {
                return NotFound();
            }
            story = found;
        }
        else
        {
            story = new StoryBankItem
            {
                UserId = CurrentUserId,
                CreatedUtc = DateTime.UtcNow
            };
            _context.StoryBankItems.Add(story);
        }

        story.Title = Truncate(title.Trim(), 200)!;
        story.Tags = Truncate(NullIfBlank(tags), 500);
        story.Situation = Truncate(NullIfBlank(situation), 2000);
        story.Task = Truncate(NullIfBlank(task), 2000);
        story.Action = Truncate(NullIfBlank(action), 3000);
        story.Result = Truncate(NullIfBlank(result), 2000);
        story.UpdatedUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = id is > 0
            ? $"Updated STAR story \"{story.Title}\"."
            : $"Added STAR story \"{story.Title}\" to your Story Bank.";
        return RedirectAfterStory(returnUrl);
    }

    // POST: Interviews/DeleteStory/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStory(int id)
    {
        var story = await _context.StoryBankItems
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == CurrentUserId);
        if (story is null)
        {
            return NotFound();
        }

        _context.StoryBankItems.Remove(story);
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Deleted STAR story \"{story.Title}\".";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult RedirectAfterStory(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
