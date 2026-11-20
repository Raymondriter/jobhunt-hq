using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

[Authorize]
public class ResumesController : Controller
{
    private readonly ApplicationDbContext _context;

    public ResumesController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private IQueryable<ResumeVersion> MyResumes() =>
        _context.ResumeVersions.Where(r => r.UserId == CurrentUserId);

    private async Task<ResumeVersion?> FindMineAsync(int id) =>
        await MyResumes().FirstOrDefaultAsync(r => r.Id == id);

    // GET: Resumes
    public async Task<IActionResult> Index()
    {
        var resumes = await MyResumes()
            .OrderBy(r => r.Name)
            .ToListAsync();
        return View(resumes);
    }

    // GET: Resumes/Create
    public IActionResult Create()
    {
        return View(new ResumeFormViewModel());
    }

    // POST: Resumes/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ResumeFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var resume = new ResumeVersion
        {
            UserId = CurrentUserId,
            CreatedUtc = DateTime.UtcNow
        };
        model.ApplyTo(resume);

        _context.ResumeVersions.Add(resume);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // GET: Resumes/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var resume = await FindMineAsync(id);
        if (resume == null)
        {
            return NotFound();
        }
        return View(ResumeFormViewModel.FromEntity(resume));
    }

    // POST: Resumes/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ResumeFormViewModel model)
    {
        if (id != model.Id)
        {
            return NotFound();
        }

        var resume = await FindMineAsync(id);
        if (resume == null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.ApplyTo(resume);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // GET: Resumes/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var resume = await FindMineAsync(id);
        if (resume == null)
        {
            return NotFound();
        }
        return View(resume);
    }

    // POST: Resumes/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var resume = await FindMineAsync(id);
        if (resume == null)
        {
            return NotFound();
        }

        // Applications that used this resume keep their history; the FK is set to null.
        _context.ResumeVersions.Remove(resume);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
