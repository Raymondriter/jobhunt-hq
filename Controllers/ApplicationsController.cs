using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ApplicationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Base query for the signed-in user's applications — every read starts here.</summary>
    private IQueryable<JobApplication> MyApplications() =>
        _context.JobApplications
            .Include(a => a.ResumeVersion)
            .Where(a => a.UserId == CurrentUserId);

    /// <summary>Fetch one of the current user's applications, or null (→ NotFound).</summary>
    private async Task<JobApplication?> FindMineAsync(int id) =>
        await MyApplications().FirstOrDefaultAsync(a => a.Id == id);

    // GET: Applications
    public async Task<IActionResult> Index(ApplicationStatus? status)
    {
        var query = MyApplications();
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        var model = new ApplicationsIndexViewModel
        {
            Applications = await query
                .OrderByDescending(a => a.UpdatedUtc)
                .ToListAsync(),
            StatusFilter = status,
            Today = DateOnly.FromDateTime(DateTime.Today)
        };
        return View(model);
    }

    // GET: Applications/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }
        return View(application);
    }

    // GET: Applications/Create
    public async Task<IActionResult> Create()
    {
        var model = new ApplicationFormViewModel
        {
            ResumeOptions = await GetResumeOptionsAsync(null)
        };
        return View(model);
    }

    // POST: Applications/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApplicationFormViewModel model)
    {
        if (!await ResumeBelongsToMeAsync(model.ResumeVersionId))
        {
            ModelState.AddModelError(nameof(model.ResumeVersionId), "Please choose one of your own resume versions.");
        }

        if (!ModelState.IsValid)
        {
            model.ResumeOptions = await GetResumeOptionsAsync(model.ResumeVersionId);
            return View(model);
        }

        var application = new JobApplication
        {
            UserId = CurrentUserId,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        model.ApplyTo(application);

        _context.JobApplications.Add(application);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // GET: Applications/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var model = ApplicationFormViewModel.FromEntity(application);
        model.ResumeOptions = await GetResumeOptionsAsync(application.ResumeVersionId);
        return View(model);
    }

    // POST: Applications/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ApplicationFormViewModel model)
    {
        if (id != model.Id)
        {
            return NotFound();
        }

        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        if (!await ResumeBelongsToMeAsync(model.ResumeVersionId))
        {
            ModelState.AddModelError(nameof(model.ResumeVersionId), "Please choose one of your own resume versions.");
        }

        if (!ModelState.IsValid)
        {
            model.ResumeOptions = await GetResumeOptionsAsync(model.ResumeVersionId);
            return View(model);
        }

        // UserId and CreatedUtc are preserved — ApplyTo only touches editable fields.
        model.ApplyTo(application);
        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // GET: Applications/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }
        return View(application);
    }

    // POST: Applications/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        _context.JobApplications.Remove(application);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<SelectListItem>> GetResumeOptionsAsync(int? selectedId)
    {
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderBy(r => r.Name)
            .ToListAsync();

        return resumes
            .Select(r => new SelectListItem(r.Name, r.Id.ToString(), r.Id == selectedId))
            .ToList();
    }

    private async Task<bool> ResumeBelongsToMeAsync(int? resumeVersionId)
    {
        if (resumeVersionId == null)
        {
            return true;
        }
        return await _context.ResumeVersions
            .AnyAsync(r => r.Id == resumeVersionId.Value && r.UserId == CurrentUserId);
    }
}
