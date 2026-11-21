using System.Security.Claims;
using System.Text;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly FitCheckService _fitCheck;
    private readonly AiPacketService _packet;

    public ApplicationsController(ApplicationDbContext context, FitCheckService fitCheck, AiPacketService packet)
    {
        _context = context;
        _fitCheck = fitCheck;
        _packet = packet;
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
        await ApplyFitCheckAsync(application);

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
        await ApplyFitCheckAsync(application);
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

    // GET: Applications/AddPosting
    public IActionResult AddPosting()
    {
        return View(new AddPostingViewModel());
    }

    // POST: Applications/AddPosting — capture a posting as a Saved application
    // and run the fit check against the user's search profile immediately.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPosting(AddPostingViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var application = new JobApplication
        {
            UserId = CurrentUserId,
            Company = model.Company,
            RoleTitle = model.JobTitle,
            PostingUrl = model.PostingUrl,
            // Mirror into the older free-form Url so existing pages link the posting too.
            Url = model.PostingUrl,
            PostingText = model.PostingText,
            PayMin = model.PayMin,
            PayMax = model.PayMax,
            Status = ApplicationStatus.Saved,
            DateSaved = DateOnly.FromDateTime(DateTime.Today),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        await ApplyFitCheckAsync(application);

        _context.JobApplications.Add(application);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = application.Id });
    }

    // POST: Applications/RecheckFit/5 — re-run the fit check on demand
    // (e.g. after the search profile changed).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecheckFit(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        await ApplyFitCheckAsync(application);
        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    // GET: Applications/Prepare/5 — the preparation view: posting, fit check,
    // must-have checklist, standing answers, resumes, and the AI packet.
    public async Task<IActionResult> Prepare(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var model = await BuildPrepareModelAsync(application);
        return View(model);
    }

    // GET: Applications/DownloadPacket/5 — the AI packet as a Markdown file.
    public async Task<IActionResult> DownloadPacket(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var model = await BuildPrepareModelAsync(application);
        var bytes = Encoding.UTF8.GetBytes(model.PacketMarkdown);
        var fileName = $"{SanitizeFileName(application.Company)}-{SanitizeFileName(application.RoleTitle)}-ai-packet.md";
        return File(bytes, "text/markdown; charset=utf-8", fileName);
    }

    private async Task<PrepareViewModel> BuildPrepareModelAsync(JobApplication application)
    {
        var profile = await GetMyProfileAsync();
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderByDescending(r => r.CreatedUtc)
            .ToListAsync();
        var chosenResume = application.ResumeVersion ?? resumes.FirstOrDefault();

        return new PrepareViewModel
        {
            Application = application,
            Profile = profile,
            Resumes = resumes,
            ChosenResume = chosenResume,
            MustHaveChecks = _fitCheck.CheckMustHaves(profile, application.PostingText),
            PacketMarkdown = _packet.BuildMarkdown(application, profile, chosenResume),
            Today = DateOnly.FromDateTime(DateTime.Today)
        };
    }

    private async Task<SearchProfile?> GetMyProfileAsync() =>
        await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == CurrentUserId);

    /// <summary>
    /// Runs the fit check against the current user's profile and stores the
    /// verdict + reasons on the application. Applications with posting text
    /// are always (re)checked; without posting text a check still runs (skip
    /// list and pay rules apply), except that a brand-new application with no
    /// posting material at all is left unchecked.
    /// </summary>
    private async Task ApplyFitCheckAsync(JobApplication application)
    {
        var hasPostingMaterial = !string.IsNullOrWhiteSpace(application.PostingText)
            || !string.IsNullOrWhiteSpace(application.PostingUrl)
            || application.PayMin.HasValue
            || application.PayMax.HasValue
            || application.FitVerdict is not null;
        if (!hasPostingMaterial)
        {
            return;
        }

        var profile = await GetMyProfileAsync();
        var result = _fitCheck.Evaluate(application, profile);
        application.FitVerdict = result.Verdict;
        application.FitReasons = result.ReasonsText;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "application" : cleaned;
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
