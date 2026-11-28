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
    private readonly InterviewPacketService _interviewPacket;
    private readonly PostingFetchService _postingFetch;

    public ApplicationsController(
        ApplicationDbContext context,
        FitCheckService fitCheck,
        AiPacketService packet,
        InterviewPacketService interviewPacket,
        PostingFetchService postingFetch)
    {
        _context = context;
        _fitCheck = fitCheck;
        _packet = packet;
        _interviewPacket = interviewPacket;
        _postingFetch = postingFetch;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Base query for the signed-in user's applications — every read starts here.</summary>
    private IQueryable<JobApplication> MyApplications() =>
        _context.JobApplications
            .Include(a => a.ResumeVersion)
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == CurrentUserId);

    /// <summary>Fetch one of the current user's applications, or null (→ NotFound).</summary>
    private async Task<JobApplication?> FindMineAsync(int id) =>
        await MyApplications().FirstOrDefaultAsync(a => a.Id == id);

    // GET: Applications
    public async Task<IActionResult> Index(
        ApplicationStatus? status,
        string? q,
        string? fitFilter,
        string? dueFilter,
        string? sortBy)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dueSoonLimit = today.AddDays(7);
        var baseQuery = MyApplications();
        var totalCount = await baseQuery.CountAsync();

        var query = baseQuery;
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(a =>
                a.Company.Contains(term)
                || a.RoleTitle.Contains(term)
                || (a.Location != null && a.Location.Contains(term))
                || (a.NextAction != null && a.NextAction.Contains(term))
                || (a.Notes != null && a.Notes.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(fitFilter))
        {
            if (string.Equals(fitFilter, FitCheckService.VerdictNotChecked, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(a => a.FitVerdict == null || a.FitVerdict == FitCheckService.VerdictNotChecked);
            }
            else
            {
                query = query.Where(a => a.FitVerdict == fitFilter);
            }
        }

        if (!string.IsNullOrWhiteSpace(dueFilter))
        {
            var openStatuses = new[]
            {
                ApplicationStatus.Saved,
                ApplicationStatus.Applied,
                ApplicationStatus.Screening,
                ApplicationStatus.Interview,
                ApplicationStatus.Offer
            };
            if (string.Equals(dueFilter, "overdue", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(a =>
                    a.NextActionDue != null
                    && a.NextActionDue < today
                    && openStatuses.Contains(a.Status));
            }
            else if (string.Equals(dueFilter, "due-soon", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(a =>
                    a.NextActionDue != null
                    && a.NextActionDue <= dueSoonLimit
                    && openStatuses.Contains(a.Status));
            }
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "updated" : sortBy.Trim().ToLowerInvariant();
        query = normalizedSort switch
        {
            "due" => query
                .OrderBy(a => a.NextActionDue == null)
                .ThenBy(a => a.NextActionDue)
                .ThenByDescending(a => a.UpdatedUtc),
            "applied" => query
                .OrderBy(a => a.DateApplied == null)
                .ThenByDescending(a => a.DateApplied)
                .ThenByDescending(a => a.UpdatedUtc),
            "company" => query
                .OrderBy(a => a.Company)
                .ThenBy(a => a.RoleTitle),
            _ => query.OrderByDescending(a => a.UpdatedUtc)
        };

        var model = new ApplicationsIndexViewModel
        {
            Applications = await query.ToListAsync(),
            TotalCount = totalCount,
            StatusFilter = status,
            SearchQuery = q,
            FitFilter = fitFilter,
            DueFilter = dueFilter,
            SortBy = normalizedSort,
            Today = today
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
        if (application.Status == ApplicationStatus.Applied && !application.DateApplied.HasValue)
        {
            application.DateApplied = DateOnly.FromDateTime(DateTime.Today);
        }
        await ApplyFitCheckAsync(application);

        _context.JobApplications.Add(application);
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Saved application for {application.Company} — {application.RoleTitle}.";
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
        if (application.Status == ApplicationStatus.Applied && !application.DateApplied.HasValue)
        {
            application.DateApplied = DateOnly.FromDateTime(DateTime.Today);
        }
        application.UpdatedUtc = DateTime.UtcNow;
        await ApplyFitCheckAsync(application);
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Updated {application.Company} — {application.RoleTitle}.";
        return RedirectToAction(nameof(Details), new { id = application.Id });
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
        TempData["StatusMessage"] = $"Deleted {application.Company} — {application.RoleTitle}.";
        return RedirectToAction(nameof(Index));
    }

    // GET: Applications/AddPosting — also accepts pre-filled query parameters from
    // the 1-click "Save to JobHunt HQ" browser bookmarklet (never auto-saves).
    public async Task<IActionResult> AddPosting(
        string? url = null,
        string? company = null,
        string? title = null,
        string? location = null,
        string? text = null)
    {
        var parsedCompany = TruncateText(company, 200) ?? string.Empty;
        var parsedTitle = TruncateText(title, 200) ?? string.Empty;

        // Clean common job-board suffixes (" | LinkedIn", " - Indeed.com") and split "Role at Company".
        if (!string.IsNullOrWhiteSpace(parsedTitle))
        {
            parsedTitle = System.Text.RegularExpressions.Regex.Replace(
                parsedTitle,
                @"\s*[\|\-–—]\s*(LinkedIn|Indeed|Glassdoor|Greenhouse|Lever|Ashby|Workday|Jobs)\b.*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            if (string.IsNullOrWhiteSpace(parsedCompany))
            {
                var atIdx = parsedTitle.LastIndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                if (atIdx > 2 && atIdx < parsedTitle.Length - 5)
                {
                    parsedCompany = parsedTitle[(atIdx + 4)..].Trim();
                    parsedTitle = parsedTitle[..atIdx].Trim();
                }
            }
        }

        var fromBookmarklet = !string.IsNullOrWhiteSpace(url)
            || !string.IsNullOrWhiteSpace(parsedTitle)
            || !string.IsNullOrWhiteSpace(text);

        var model = new AddPostingViewModel
        {
            PostingUrl = TruncateText(url, 1000),
            Company = parsedCompany,
            JobTitle = parsedTitle,
            Location = TruncateText(location, 200),
            PostingText = TruncateText(text, 12000),
            FetchNote = fromBookmarklet
                ? "Captured from your browser bookmarklet — review the fields below (or click Fetch from URL for full text) and save."
                : null,
            ResumeOptions = await GetResumeOptionsAsync(null)
        };
        return View(model);
    }

    // POST: Applications/AddPosting — capture a posting as a Saved application
    // and run the fit check against the user's search profile immediately.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPosting(AddPostingViewModel model)
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
            Company = model.Company,
            RoleTitle = model.JobTitle,
            PostingUrl = model.PostingUrl,
            // Mirror into the older free-form Url so existing pages link the posting too.
            Url = model.PostingUrl,
            Location = model.Location,
            ResumeVersionId = model.ResumeVersionId,
            PostingText = model.PostingText,
            PayMin = model.PayMin,
            PayMax = model.PayMax,
            SalaryMin = model.PayMin,
            SalaryMax = model.PayMax,
            Status = ApplicationStatus.Saved,
            DateSaved = DateOnly.FromDateTime(DateTime.Today),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        await ApplyFitCheckAsync(application);

        _context.JobApplications.Add(application);
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Posting captured — fit check verdict: {application.FitVerdict ?? FitCheckService.VerdictNotChecked}.";
        return RedirectToAction(nameof(Prepare), new { id = application.Id });
    }

    // POST: Applications/FetchPosting — "Fetch from URL" on the Add posting
    // page. Fetches the posting page server-side (SSRF-guarded, see
    // PostingFetchService) and fills the form for review. NEVER saves.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FetchPosting(AddPostingViewModel model)
    {
        var result = await _postingFetch.FetchAsync(model.PostingUrl);
        if (result.Success)
        {
            model.PostingText = result.Text;
            if (string.IsNullOrWhiteSpace(model.JobTitle) && !string.IsNullOrWhiteSpace(result.Title))
            {
                model.JobTitle = result.Title;
            }
            model.FetchNote = PostingFetchService.SuccessMessage;
        }
        else
        {
            model.FetchNote = PostingFetchService.FailureMessage;
        }

        // The view renders inputs from ModelState's attempted values when
        // present — drop the entries we just filled server-side so the
        // fetched values show.
        ModelState.Remove(nameof(model.PostingText));
        ModelState.Remove(nameof(model.JobTitle));
        model.ResumeOptions = await GetResumeOptionsAsync(model.ResumeVersionId);
        return View(nameof(AddPosting), model);
    }

    // POST: Applications/FetchPostingForEdit/5 — "Fetch from URL" on the Edit
    // page so existing/CSV-imported applications can backfill their posting text
    // in one click using the same SSRF-guarded fetcher. Never saves until the
    // user reviews and clicks Save changes.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FetchPostingForEdit(int id, ApplicationFormViewModel model)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var urlToFetch = !string.IsNullOrWhiteSpace(model.PostingUrl) ? model.PostingUrl : model.Url;
        var result = await _postingFetch.FetchAsync(urlToFetch);
        if (result.Success)
        {
            model.PostingText = result.Text;
            if (string.IsNullOrWhiteSpace(model.RoleTitle) && !string.IsNullOrWhiteSpace(result.Title))
            {
                model.RoleTitle = result.Title;
            }
            model.FetchNote = PostingFetchService.SuccessMessage;
        }
        else
        {
            model.FetchNote = PostingFetchService.FailureMessage;
        }

        ModelState.Remove(nameof(model.PostingText));
        ModelState.Remove(nameof(model.RoleTitle));
        model.Id = id;
        model.ResumeOptions = await GetResumeOptionsAsync(model.ResumeVersionId);
        return View(nameof(Edit), model);
    }

    // POST: Applications/RecheckFit/5 — re-run the fit check on demand
    // (e.g. after the search profile changed).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecheckFit(int id, string? returnAction = null)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        await ApplyFitCheckAsync(application);
        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Re-checked fit against your search profile: {application.FitVerdict ?? FitCheckService.VerdictNotChecked}.";
        if (string.Equals(returnAction, nameof(Prepare), StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Prepare), new { id });
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: Applications/UpdatePreparation/5 — two-way Prepare workspace update:
    // lets the user switch the resume version for the packet, update the status
    // (auto-filling DateApplied when moving to Applied), and record the next
    // action, due date, and notes returned from their AI preparation session.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePreparation(
        int id,
        ApplicationStatus status,
        int? resumeVersionId,
        string? nextAction,
        DateOnly? nextActionDue,
        string? confirmationReference,
        string? blockingQuestion,
        string? notes)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        if (!await ResumeBelongsToMeAsync(resumeVersionId))
        {
            TempData["ErrorMessage"] = "Please choose one of your own resume versions.";
            return RedirectToAction(nameof(Prepare), new { id });
        }

        if (Enum.IsDefined(status))
        {
            application.Status = status;
            if (status == ApplicationStatus.Applied && !application.DateApplied.HasValue)
            {
                application.DateApplied = DateOnly.FromDateTime(DateTime.Today);
            }
        }

        application.ResumeVersionId = resumeVersionId;
        application.NextAction = string.IsNullOrWhiteSpace(nextAction)
            ? null
            : nextAction.Trim()[..Math.Min(500, nextAction.Trim().Length)];
        application.NextActionDue = nextActionDue;
        application.ConfirmationReference = string.IsNullOrWhiteSpace(confirmationReference)
            ? null
            : confirmationReference.Trim()[..Math.Min(200, confirmationReference.Trim().Length)];
        application.BlockingQuestion = string.IsNullOrWhiteSpace(blockingQuestion)
            ? null
            : blockingQuestion.Trim()[..Math.Min(1000, blockingQuestion.Trim().Length)];
        application.Notes = string.IsNullOrWhiteSpace(notes)
            ? null
            : notes.Trim()[..Math.Min(4000, notes.Trim().Length)];
        application.UpdatedUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Preparation details and AI packet updated.";
        return RedirectToAction(nameof(Prepare), new { id });
    }

    // POST: Applications/CompleteFollowUp/5 — one-click completion of a follow-up
    // from the Dashboard or Applications list.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteFollowUp(int id, string? returnUrl = null)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var completedAction = application.NextAction;
        application.NextAction = null;
        application.NextActionDue = null;
        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["StatusMessage"] = string.IsNullOrWhiteSpace(completedAction)
            ? $"Cleared follow-up for {application.Company}."
            : $"Completed follow-up for {application.Company}: \"{completedAction}\".";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
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

        var packet = await BuildPacketDataAsync(application);
        var bytes = Encoding.UTF8.GetBytes(_packet.BuildMarkdown(packet));
        var fileName = $"{SanitizeFileName(application.Company)}-{SanitizeFileName(application.RoleTitle)}-ai-packet.md";
        return File(bytes, "text/markdown; charset=utf-8", fileName);
    }

    // GET: Applications/DownloadPacketJson/5 — the same packet as structured
    // JSON, for programmatic agents.
    public async Task<IActionResult> DownloadPacketJson(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var packet = await BuildPacketDataAsync(application);
        var bytes = Encoding.UTF8.GetBytes(_packet.BuildJson(packet));
        var fileName = $"{SanitizeFileName(application.Company)}-{SanitizeFileName(application.RoleTitle)}-ai-packet.json";
        return File(bytes, "application/json; charset=utf-8", fileName);
    }

    // ---- M5: Interview Prep Workspace, Live Call Cheat Sheet & Interview AI Packet ----

    // POST: Applications/BulkAction — fast multi-select triage for 50–100+ pipelines.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAction(int[]? selectedIds, string? bulkAction)
    {
        if (selectedIds is null || selectedIds.Length == 0 || string.IsNullOrWhiteSpace(bulkAction))
        {
            TempData["ErrorMessage"] = "Select at least one application and choose a bulk action.";
            return RedirectToAction(nameof(Index));
        }

        var idSet = selectedIds.Distinct().ToList();
        var apps = await _context.JobApplications
            .Where(a => a.UserId == CurrentUserId && idSet.Contains(a.Id))
            .ToListAsync();

        if (apps.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.UtcNow;
        var label = bulkAction.Trim().ToLowerInvariant();
        var summary = "";

        foreach (var app in apps)
        {
            switch (label)
            {
                case "followup3":
                    app.NextAction = string.IsNullOrWhiteSpace(app.NextAction) ? "Send brief status follow-up" : app.NextAction;
                    app.NextActionDue = today.AddDays(3);
                    summary = $"Scheduled 3-day follow-up for {apps.Count} application{(apps.Count == 1 ? "" : "s")}.";
                    break;
                case "applied":
                    app.Status = ApplicationStatus.Applied;
                    app.DateApplied ??= today;
                    summary = $"Marked {apps.Count} application{(apps.Count == 1 ? "" : "s")} as Applied.";
                    break;
                case "ghosted":
                    app.Status = ApplicationStatus.Ghosted;
                    app.NextAction = null;
                    app.NextActionDue = null;
                    summary = $"Marked {apps.Count} application{(apps.Count == 1 ? "" : "s")} as Ghosted.";
                    break;
                case "rejected":
                    app.Status = ApplicationStatus.Rejected;
                    app.NextAction = null;
                    app.NextActionDue = null;
                    summary = $"Marked {apps.Count} application{(apps.Count == 1 ? "" : "s")} as Rejected.";
                    break;
                default:
                    TempData["ErrorMessage"] = "Unknown bulk action.";
                    return RedirectToAction(nameof(Index));
            }
            app.UpdatedUtc = now;
        }

        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = summary;
        return RedirectToAction(nameof(Index));
    }

    // POST: Applications/QuickTriage/5 — 1-click follow-up or ghosted action from Dashboard Quiet Radar.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickTriage(int id, string? mode)
    {
        var app = await FindMineAsync(id);
        if (app is null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (string.Equals(mode, "ghosted", StringComparison.OrdinalIgnoreCase))
        {
            app.Status = ApplicationStatus.Ghosted;
            app.NextAction = null;
            app.NextActionDue = null;
            TempData["StatusMessage"] = $"Marked {app.Company} — {app.RoleTitle} as Ghosted.";
        }
        else
        {
            app.NextAction = "Send polite follow-up note";
            app.NextActionDue = today.AddDays(3);
            TempData["StatusMessage"] = $"Scheduled a 3-day follow-up for {app.Company} ({app.NextActionDue:yyyy-MM-dd}).";
        }
        app.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return RedirectToAction("Dashboard", "Home");
    }

    // POST: Applications/PromoteToInterview/5 — one-click move into Interview stage
    // and open the Interview Prep Workspace.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteToInterview(int id)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        if (application.Status != ApplicationStatus.Interview && application.Status != ApplicationStatus.Offer)
        {
            application.Status = ApplicationStatus.Interview;
            if (!application.DateApplied.HasValue)
            {
                application.DateApplied = DateOnly.FromDateTime(DateTime.Today);
            }
            application.UpdatedUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        TempData["StatusMessage"] = $"Moved {application.Company} — {application.RoleTitle} to Interview. Schedule your round and build your prep brief below.";
        return RedirectToAction(nameof(Interview), new { id });
    }

    // GET: Applications/Interview/5 — per-application Interview Prep Workspace
    // (or Live Call Cheat Sheet HUD when live=true).
    public async Task<IActionResult> Interview(
        int id,
        bool live = false,
        int? editRoundId = null,
        int? focusRoundId = null)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var model = await BuildInterviewModelAsync(application, live, editRoundId, focusRoundId);
        return View(model);
    }

    // POST: Applications/SaveInterviewOverview/5 — save 60-second pitch, company
    // research, resume version, and status on the Interview Workspace.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveInterviewOverview(
        int id,
        ApplicationStatus status,
        int? resumeVersionId,
        string? elevatorPitch,
        string? companyResearch)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        if (!await ResumeBelongsToMeAsync(resumeVersionId))
        {
            TempData["ErrorMessage"] = "Please choose one of your own resume versions.";
            return RedirectToAction(nameof(Interview), new { id });
        }

        if (Enum.IsDefined(status))
        {
            application.Status = status;
            if ((status == ApplicationStatus.Applied || status == ApplicationStatus.Screening || status == ApplicationStatus.Interview)
                && !application.DateApplied.HasValue)
            {
                application.DateApplied = DateOnly.FromDateTime(DateTime.Today);
            }
        }

        application.ResumeVersionId = resumeVersionId;
        application.ElevatorPitch = TruncateText(elevatorPitch, 2000);
        application.CompanyResearch = TruncateText(companyResearch, 4000);
        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["StatusMessage"] = "Interview pitch, research, and packet context saved.";
        return RedirectToAction(nameof(Interview), new { id });
    }

    // POST: Applications/SaveInterviewRound/5 — add or update an interview round.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveInterviewRound(
        int id,
        int? roundId,
        int roundNumber,
        string roundType,
        DateTime? scheduledLocal,
        string? timeZoneLabel,
        int durationMinutes,
        string? meetingUrl,
        string? interviewers,
        string? prepNotes,
        string? questionsToAsk,
        bool syncNextAction = true)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        InterviewRound round;
        if (roundId is int existingRoundId && existingRoundId > 0)
        {
            var found = application.InterviewRounds.FirstOrDefault(r => r.Id == existingRoundId);
            if (found is null)
            {
                return NotFound();
            }
            round = found;
        }
        else
        {
            round = new InterviewRound
            {
                UserId = CurrentUserId,
                JobApplicationId = application.Id,
                CreatedUtc = DateTime.UtcNow
            };
            _context.InterviewRounds.Add(round);
        }

        round.RoundNumber = Math.Clamp(roundNumber <= 0 ? 1 : roundNumber, 1, 20);
        round.RoundType = string.IsNullOrWhiteSpace(roundType)
            ? "Interview"
            : TruncateText(roundType, 100)!;
        round.ScheduledLocal = scheduledLocal;
        round.TimeZoneLabel = TruncateText(timeZoneLabel, 60);
        round.DurationMinutes = Math.Clamp(durationMinutes <= 0 ? 45 : durationMinutes, 10, 480);
        round.MeetingUrl = TruncateText(meetingUrl, 1000);
        round.Interviewers = TruncateText(interviewers, 500);
        round.PrepNotes = TruncateText(prepNotes, 4000);
        round.QuestionsToAsk = TruncateText(questionsToAsk, 4000);
        round.UpdatedUtc = DateTime.UtcNow;

        // If the application was still in Saved or Applied, promote it automatically.
        if (application.Status is ApplicationStatus.Saved or ApplicationStatus.Applied)
        {
            application.Status = round.RoundType.Contains("Screen", StringComparison.OrdinalIgnoreCase)
                ? ApplicationStatus.Screening
                : ApplicationStatus.Interview;
            if (!application.DateApplied.HasValue)
            {
                application.DateApplied = DateOnly.FromDateTime(DateTime.Today);
            }
        }

        if (syncNextAction && scheduledLocal.HasValue && !round.Completed)
        {
            application.NextAction = $"Prepare for Round {round.RoundNumber}: {round.RoundType}";
            application.NextActionDue = DateOnly.FromDateTime(scheduledLocal.Value);
        }

        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["StatusMessage"] = roundId is > 0
            ? $"Updated Round {round.RoundNumber} ({round.RoundType})."
            : $"Scheduled Round {round.RoundNumber} ({round.RoundType}) for {application.Company}.";
        return RedirectToAction(nameof(Interview), new { id, focusRoundId = round.Id });
    }

    // POST: Applications/SaveRoundDebrief/5 — save live call scratchpad / debrief
    // notes and optionally set a post-interview thank-you or check-in follow-up.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRoundDebrief(
        int id,
        int roundId,
        string? debriefNotes,
        bool completed = false,
        string? followUpPreset = null,
        bool live = false)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var round = application.InterviewRounds.FirstOrDefault(r => r.Id == roundId);
        if (round is null)
        {
            return NotFound();
        }

        round.DebriefNotes = TruncateText(debriefNotes, 4000);
        round.Completed = completed;
        round.UpdatedUtc = DateTime.UtcNow;

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (string.Equals(followUpPreset, "24h-thank-you", StringComparison.OrdinalIgnoreCase))
        {
            round.Completed = true;
            application.NextAction = $"Send thank-you note after Round {round.RoundNumber} ({round.RoundType})";
            application.NextActionDue = today.AddDays(1);
        }
        else if (string.Equals(followUpPreset, "5d-checkin", StringComparison.OrdinalIgnoreCase))
        {
            round.Completed = true;
            application.NextAction = $"Follow up with {application.Company} on Round {round.RoundNumber} next steps";
            application.NextActionDue = today.AddDays(5);
        }

        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["StatusMessage"] = followUpPreset is not null
            ? $"Saved debrief for Round {round.RoundNumber} and scheduled your next follow-up."
            : $"Saved notes for Round {round.RoundNumber} ({round.RoundType}).";
        return RedirectToAction(nameof(Interview), new { id, live, focusRoundId = round.Id });
    }

    // POST: Applications/DeleteInterviewRound/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteInterviewRound(int id, int roundId)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var round = application.InterviewRounds.FirstOrDefault(r => r.Id == roundId);
        if (round is null)
        {
            return NotFound();
        }

        _context.InterviewRounds.Remove(round);
        application.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Removed Round {round.RoundNumber} ({round.RoundType}).";
        return RedirectToAction(nameof(Interview), new { id });
    }

    // GET: Applications/DownloadInterviewIcs/5?roundId=10
    public async Task<IActionResult> DownloadInterviewIcs(int id, int roundId)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var round = application.InterviewRounds.FirstOrDefault(r => r.Id == roundId);
        if (round is null)
        {
            return NotFound();
        }

        var cheatSheetUrl = Url.Action(
            nameof(Interview),
            "Applications",
            new { id = application.Id, live = true, focusRoundId = round.Id },
            Request.Scheme);
        var ics = InterviewPacketService.BuildIcsCalendarInvite(application, round, cheatSheetUrl);
        var fileName = $"{SanitizeFileName(application.Company)}-round-{round.RoundNumber}.ics";
        return File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", fileName);
    }

    // GET: Applications/DownloadInterviewPacket/5
    public async Task<IActionResult> DownloadInterviewPacket(int id, int? roundId = null)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var packet = await BuildInterviewPacketDataAsync(application, roundId);
        var bytes = Encoding.UTF8.GetBytes(_interviewPacket.BuildMarkdown(packet));
        var fileName = $"{SanitizeFileName(application.Company)}-{SanitizeFileName(application.RoleTitle)}-interview-packet.md";
        return File(bytes, "text/markdown; charset=utf-8", fileName);
    }

    // GET: Applications/DownloadInterviewPacketJson/5
    public async Task<IActionResult> DownloadInterviewPacketJson(int id, int? roundId = null)
    {
        var application = await FindMineAsync(id);
        if (application == null)
        {
            return NotFound();
        }

        var packet = await BuildInterviewPacketDataAsync(application, roundId);
        var bytes = Encoding.UTF8.GetBytes(_interviewPacket.BuildJson(packet));
        var fileName = $"{SanitizeFileName(application.Company)}-{SanitizeFileName(application.RoleTitle)}-interview-packet.json";
        return File(bytes, "application/json; charset=utf-8", fileName);
    }

    private async Task<InterviewPacketData> BuildInterviewPacketDataAsync(JobApplication application, int? focusRoundId)
    {
        var profile = await GetMyProfileAsync();
        var chosenResume = application.ResumeVersion ?? await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefaultAsync();

        var rounds = application.InterviewRounds
            .OrderBy(r => r.RoundNumber)
            .ThenBy(r => r.ScheduledLocal)
            .ThenBy(r => r.Id)
            .ToList();

        var focusRound = focusRoundId.HasValue
            ? rounds.FirstOrDefault(r => r.Id == focusRoundId.Value)
            : rounds.FirstOrDefault(r => !r.Completed) ?? rounds.LastOrDefault();

        var stories = await _context.StoryBankItems
            .Where(s => s.UserId == CurrentUserId)
            .OrderByDescending(s => s.UpdatedUtc)
            .ToListAsync();

        return await _interviewPacket.BuildPacketAsync(application, profile, chosenResume, rounds, focusRound, stories);
    }

    private async Task<InterviewWorkspaceViewModel> BuildInterviewModelAsync(
        JobApplication application,
        bool live,
        int? editRoundId,
        int? focusRoundId)
    {
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderByDescending(r => r.CreatedUtc)
            .ToListAsync();
        var packet = await BuildInterviewPacketDataAsync(application, focusRoundId);
        var shortPrompt = InterviewPacketService.BuildShortPrompt(application, packet.FocusRound);
        var rounds = packet.Rounds.ToList();
        var editingRound = editRoundId.HasValue
            ? rounds.FirstOrDefault(r => r.Id == editRoundId.Value)
            : null;
        var nextRoundNum = rounds.Count == 0 ? 1 : rounds.Max(r => r.RoundNumber) + 1;

        return new InterviewWorkspaceViewModel
        {
            Application = application,
            Profile = packet.Profile,
            Resumes = resumes,
            ChosenResume = packet.Resume,
            ResumeOptions = await GetResumeOptionsAsync(application.ResumeVersionId ?? packet.Resume?.Id),
            Rounds = rounds,
            FocusRound = packet.FocusRound,
            EditingRound = editingRound,
            NextRoundNumber = nextRoundNum,
            AllStories = PrepAnalysisService.RankStories(packet.Stories, packet.MatchedStoryIds, application),
            MatchedStoryIds = packet.MatchedStoryIds,
            MustHaveChecks = _fitCheck.CheckMustHaves(packet.Profile, application.PostingText),
            ResumeMatch = PrepAnalysisService.AnalyzeResumeMatch(application, packet.Profile, packet.ResumeText.Text),
            InterviewPacketMarkdown = _interviewPacket.BuildMarkdown(packet),
            ReadyPrompts = packet.ReadyPrompts,
            AiShortPrompt = shortPrompt,
            ChatGptUrl = "https://chatgpt.com/?q=" + Uri.EscapeDataString(shortPrompt),
            ClaudeUrl = "https://claude.ai/new?q=" + Uri.EscapeDataString(shortPrompt),
            LiveMode = live,
            Today = DateOnly.FromDateTime(DateTime.Today)
        };
    }

    private static string? TruncateText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>Assembles the packet data: profile, chosen resume (+ text extraction), duplicate-company count.</summary>
    private async Task<AiPacketData> BuildPacketDataAsync(JobApplication application)
    {
        var profile = await GetMyProfileAsync();
        var chosenResume = application.ResumeVersion ?? await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefaultAsync();
        var otherApplicationsAtCompany = await _context.JobApplications
            .CountAsync(a => a.UserId == CurrentUserId
                             && a.Id != application.Id
                             && a.Company == application.Company);

        return await _packet.BuildPacketAsync(application, profile, chosenResume, otherApplicationsAtCompany);
    }

    private async Task<PrepareViewModel> BuildPrepareModelAsync(JobApplication application)
    {
        var profile = await GetMyProfileAsync();
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderByDescending(r => r.CreatedUtc)
            .ToListAsync();
        var packet = await BuildPacketDataAsync(application);
        var shortPrompt = AiPacketService.BuildShortPrompt(application);

        return new PrepareViewModel
        {
            Application = application,
            Profile = profile,
            Resumes = resumes,
            ChosenResume = packet.Resume,
            MustHaveChecks = _fitCheck.CheckMustHaves(profile, application.PostingText),
            ResumeMatch = PrepAnalysisService.AnalyzeResumeMatch(application, profile, packet.ResumeText.Text),
            StandingAnswerCards = PrepAnalysisService.ParseStandingAnswers(profile?.StandingAnswers),
            PacketMarkdown = _packet.BuildMarkdown(packet),
            ReadyPrompts = packet.ReadyPrompts,
            AtsDetectedName = packet.AtsName,
            AiShortPrompt = shortPrompt,
            ChatGptUrl = "https://chatgpt.com/?q=" + Uri.EscapeDataString(shortPrompt),
            ClaudeUrl = "https://claude.ai/new?q=" + Uri.EscapeDataString(shortPrompt),
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
