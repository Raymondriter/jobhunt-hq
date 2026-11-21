using System.Security.Claims;
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
}
