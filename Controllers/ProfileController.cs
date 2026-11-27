using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

/// <summary>
/// The signed-in user's search profile (M4): one per user, holding their own
/// criteria and standing answers. Everything here is scoped to the current
/// user — the profile is found by UserId, never by a posted id.
/// </summary>
[Authorize]
public class ProfileController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly FitCheckService _fitCheck;
    private readonly UserDataPurgeService _purge;
    private readonly ResumeTextService _resumeText;

    public ProfileController(
        ApplicationDbContext context,
        FitCheckService fitCheck,
        UserDataPurgeService purge,
        ResumeTextService resumeText)
    {
        _context = context;
        _fitCheck = fitCheck;
        _purge = purge;
        _resumeText = resumeText;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<SearchProfile?> FindMineAsync() =>
        await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == CurrentUserId);

    // GET: Profile
    public async Task<IActionResult> Index()
    {
        var profile = await FindMineAsync();
        var tokens = await _context.ApiTokens
            .Where(t => t.UserId == CurrentUserId && t.RevokedUtc == null)
            .OrderByDescending(t => t.CreatedUtc)
            .ToListAsync();
        var hasResumePdf = await _context.ResumeVersions
            .AnyAsync(r => r.UserId == CurrentUserId && r.BlobName != null);
        ViewData["ApiTokens"] = tokens;
        ViewData["HasResumePdf"] = hasResumePdf;
        return View(profile);
    }

    // POST: Profile/AutoFillFromResume — zero-cost onboarding accelerator:
    // extracts real skills & role hints from the user's newest uploaded resume PDF
    // to pre-fill MustHaves and TargetRoles without overwriting custom fields.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoFillFromResume()
    {
        var resume = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId && r.BlobName != null)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefaultAsync();

        if (resume is null)
        {
            TempData["ErrorMessage"] = "Upload a resume PDF first on the Resumes page so JobHunt HQ can extract your skills automatically.";
            return RedirectToAction("Create", "Resumes");
        }

        var extracted = await _resumeText.ExtractAsync(resume);
        if (string.IsNullOrWhiteSpace(extracted.Text))
        {
            TempData["ErrorMessage"] = extracted.Note ?? "Could not extract readable text from that PDF.";
            return RedirectToAction(nameof(Index));
        }

        var skills = PrepAnalysisService.ExtractResumeSkills(extracted.Text, 12);
        var roles = PrepAnalysisService.SuggestTargetRoles(extracted.Text);
        if (skills.Count == 0 && roles.Count == 0)
        {
            TempData["ErrorMessage"] = "No recognizable technical keywords were detected in your resume PDF — you can enter them manually on Edit Profile.";
            return RedirectToAction(nameof(Edit));
        }

        var profile = await FindMineAsync();
        if (profile is null)
        {
            profile = new SearchProfile
            {
                UserId = CurrentUserId,
                CreatedUtc = DateTime.UtcNow
            };
            _context.SearchProfiles.Add(profile);
        }

        if (string.IsNullOrWhiteSpace(profile.TargetRoles) && roles.Count > 0)
        {
            profile.TargetRoles = string.Join(", ", roles);
        }

        var existingMustHaves = FitCheckService.ParseList(profile.MustHaves);
        var merged = existingMustHaves
            .Concat(skills)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        profile.MustHaves = string.Join(", ", merged);

        if (string.IsNullOrWhiteSpace(profile.RemotePreference)
            && extracted.Text.Contains("remote", StringComparison.OrdinalIgnoreCase))
        {
            profile.RemotePreference = "Remote (US)";
        }

        profile.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["StatusMessage"] = $"Auto-filled your search profile from \"{resume.Name}\" ({skills.Count} skills detected). Review or customize anytime!";
        return RedirectToAction(nameof(Index));
    }

    // POST: Profile/CreateToken — create a personal jhq_ API token, shown once.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateToken(string? name, string[]? scopes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "Give your API token a descriptive name (for example, 'Claude Code' or 'My AI Assistant').";
            return RedirectToAction(nameof(Index));
        }

        var validSet = ApiToken.AllScopes.Select(s => s.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosenScopes = (scopes ?? Array.Empty<string>())
            .Where(s => validSet.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chosenScopes.Count == 0)
        {
            TempData["ErrorMessage"] = "Select at least one capability scope for your API token.";
            return RedirectToAction(nameof(Index));
        }

        var (rawToken, displayPrefix, sha256Hex) = ApiToken.Generate();
        var entity = new ApiToken
        {
            UserId = CurrentUserId,
            Name = name.Trim()[..Math.Min(100, name.Trim().Length)],
            TokenPrefix = displayPrefix,
            TokenHash = sha256Hex,
            Scopes = string.Join(",", chosenScopes),
            CreatedUtc = DateTime.UtcNow
        };

        _context.ApiTokens.Add(entity);
        await _context.SaveChangesAsync();

        TempData["NewApiTokenPlaintext"] = rawToken;
        TempData["NewApiTokenName"] = entity.Name;
        return RedirectToAction(nameof(Index));
    }

    // POST: Profile/CreateCalendarFeed — one click: a calendar:read-only token
    // whose URL Google/Apple/Outlook Calendar can subscribe to. Shown once.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCalendarFeed()
    {
        var (rawToken, displayPrefix, sha256Hex) = ApiToken.Generate();
        _context.ApiTokens.Add(new ApiToken
        {
            UserId = CurrentUserId,
            Name = "Calendar feed",
            TokenPrefix = displayPrefix,
            TokenHash = sha256Hex,
            Scopes = ApiToken.CalendarScope,
            CreatedUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        TempData["NewCalendarFeedKey"] = rawToken;
        return Redirect(Url.Action(nameof(Index))! + "#calendar");
    }

    // POST: Profile/RevokeToken/5 — instant self-serve revocation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeToken(int id)
    {
        var token = await _context.ApiTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
        if (token is null)
        {
            return NotFound();
        }

        token.RevokedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Revoked API token \"{token.Name}\" ({token.TokenPrefix}…).";
        return RedirectToAction(nameof(Index));
    }

    // GET: Profile/Edit
    public async Task<IActionResult> Edit()
    {
        var profile = await FindMineAsync();
        var model = profile is null
            ? new SearchProfileFormViewModel()
            : SearchProfileFormViewModel.FromEntity(profile);
        return View(model);
    }

    // POST: Profile/Edit — upsert: create the profile on first save, update it after.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SearchProfileFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var profile = await FindMineAsync();
        if (profile is null)
        {
            profile = new SearchProfile
            {
                UserId = CurrentUserId,
                CreatedUtc = DateTime.UtcNow
            };
            _context.SearchProfiles.Add(profile);
        }

        // UserId and CreatedUtc are preserved — ApplyTo only touches editable fields.
        model.ApplyTo(profile);
        profile.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Search profile saved. Future and re-checked postings will use your updated criteria.";
        return RedirectToAction(nameof(Index));
    }

    // POST: Profile/RecheckAll — re-run the deterministic fit check across all
    // of the signed-in user's tracked applications against their current profile.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecheckAll()
    {
        var profile = await FindMineAsync();
        if (profile is null)
        {
            TempData["ErrorMessage"] = "Create your search profile first before running a bulk fit check.";
            return RedirectToAction(nameof(Index));
        }

        var applications = await _context.JobApplications
            .Where(a => a.UserId == CurrentUserId)
            .ToListAsync();

        var checkedCount = 0;
        foreach (var app in applications)
        {
            var result = _fitCheck.Evaluate(app, profile);
            app.FitVerdict = result.Verdict;
            app.FitReasons = result.ReasonsText;
            checkedCount++;
        }

        if (checkedCount > 0)
        {
            await _context.SaveChangesAsync();
        }

        TempData["StatusMessage"] = checkedCount == 0
            ? "No applications tracked yet to re-check."
            : $"Re-checked fit across all {checkedCount} application{(checkedCount == 1 ? "" : "s")} against your search profile.";
        return RedirectToAction(nameof(Index));
    }

    // GET: Profile/Export — "Download my data" (phase 3): one JSON file with
    // the signed-in user's search profile, all of their applications (all
    // fields), and resume version metadata. Every query is scoped to the
    // current user, like everything else in this controller. Resume PDF bytes
    // are not included — those are downloaded from the Resumes page.
    public async Task<IActionResult> Export()
    {
        var profile = await FindMineAsync();
        var applications = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == CurrentUserId)
            .OrderBy(a => a.Id)
            .ToListAsync();
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderBy(r => r.Id)
            .ToListAsync();
        var stories = await _context.StoryBankItems
            .Where(s => s.UserId == CurrentUserId)
            .OrderBy(s => s.Id)
            .ToListAsync();

        var export = new UserDataExport
        {
            ExportedAtUtc = DateTime.UtcNow,
            AccountEmail = User.Identity?.Name ?? string.Empty,
            SearchProfile = profile is null ? null : SearchProfileExport.FromEntity(profile),
            Applications = applications.Select(ApplicationExport.FromEntity).ToList(),
            ResumeVersions = resumes.Select(ResumeVersionExport.FromEntity).ToList(),
            StoryBank = stories.Select(StoryBankExport.FromEntity).ToList()
        };

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", "jobhunt-hq-my-data.json");
    }

    // GET: Profile/ExportCsv — round-trip CSV export of the user's applications
    // matching the /Import column format.
    public async Task<IActionResult> ExportCsv()
    {
        var applications = await _context.JobApplications
            .Include(a => a.ResumeVersion)
            .Where(a => a.UserId == CurrentUserId)
            .OrderByDescending(a => a.UpdatedUtc)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Company,Title,Url,Location,Status,DateApplied,NextAction,NextActionDue,SalaryMin,SalaryMax,ResumeName,FitVerdict,ConfirmationReference,Notes");
        foreach (var a in applications)
        {
            var fields = new[]
            {
                CsvEscape(a.Company),
                CsvEscape(a.RoleTitle),
                CsvEscape(a.PostingUrl ?? a.Url),
                CsvEscape(a.Location),
                CsvEscape(a.Status.ToString()),
                CsvEscape(a.DateApplied?.ToString("yyyy-MM-dd")),
                CsvEscape(a.NextAction),
                CsvEscape(a.NextActionDue?.ToString("yyyy-MM-dd")),
                CsvEscape((a.PayMin ?? a.SalaryMin)?.ToString()),
                CsvEscape((a.PayMax ?? a.SalaryMax)?.ToString()),
                CsvEscape(a.ResumeVersion?.Name),
                CsvEscape(a.FitVerdict),
                CsvEscape(a.ConfirmationReference),
                CsvEscape(a.Notes)
            };
            sb.AppendLine(string.Join(",", fields));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", "jobhunt-hq-applications.csv");
    }

    // POST: Profile/DeleteAllMyData — self-serve wipe of all user data + uploaded
    // resume blobs so closing an account does not require manual item-by-item deletes.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAllMyData(string? confirmText)
    {
        if (!string.Equals(confirmText?.Trim(), "DELETE", StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "Type DELETE in all caps to confirm wiping your tracked data.";
            return RedirectToAction(nameof(Index));
        }

        await _purge.PurgeUserDataAsync(CurrentUserId);
        TempData["StatusMessage"] = "All of your applications, interview rounds, STAR stories, resumes, and search profile have been permanently deleted.";
        return RedirectToAction(nameof(Index));
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
