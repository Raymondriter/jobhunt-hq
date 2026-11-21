using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
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

    public ProfileController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<SearchProfile?> FindMineAsync() =>
        await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == CurrentUserId);

    // GET: Profile
    public async Task<IActionResult> Index()
    {
        var profile = await FindMineAsync();
        return View(profile);
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
            .Where(a => a.UserId == CurrentUserId)
            .OrderBy(a => a.Id)
            .ToListAsync();
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .OrderBy(r => r.Id)
            .ToListAsync();

        var export = new UserDataExport
        {
            ExportedAtUtc = DateTime.UtcNow,
            AccountEmail = User.Identity?.Name ?? string.Empty,
            SearchProfile = profile is null ? null : SearchProfileExport.FromEntity(profile),
            Applications = applications.Select(ApplicationExport.FromEntity).ToList(),
            ResumeVersions = resumes.Select(ResumeVersionExport.FromEntity).ToList()
        };

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", "jobhunt-hq-my-data.json");
    }
}
