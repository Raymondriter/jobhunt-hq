using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

[Authorize]
public class ResumesController : Controller
{
    private const long MaxFileBytes = 10 * 1024 * 1024; // 10 MB

    private readonly ApplicationDbContext _context;
    private readonly IResumeStorageService _storage;

    public ResumesController(ApplicationDbContext context, IResumeStorageService storage)
    {
        _context = context;
        _storage = storage;
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

        // Don't leave the uploaded file orphaned in Blob Storage.
        if (resume.BlobName != null)
        {
            await _storage.DeleteAsync(resume.BlobName);
        }

        // Applications that used this resume keep their history; the FK is set to null.
        _context.ResumeVersions.Remove(resume);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // POST: Resumes/Upload/5 — attach (or replace) the PDF file for a resume version.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(int id, IFormFile? file)
    {
        var resume = await FindMineAsync(id);
        if (resume == null)
        {
            return NotFound();
        }

        if (file == null || file.Length == 0)
        {
            TempData["FileError"] = "Choose a PDF file to upload.";
            return RedirectToAction(nameof(Index));
        }

        // PDF only — check both the extension and the reported content type.
        var isPdf = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            && file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf)
        {
            TempData["FileError"] = "Only PDF files can be uploaded.";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > MaxFileBytes)
        {
            TempData["FileError"] = "That file is over the 10 MB limit.";
            return RedirectToAction(nameof(Index));
        }

        var oldBlobName = resume.BlobName;
        var newBlobName = $"{CurrentUserId}/{resume.Id}/{Guid.NewGuid()}.pdf";

        await using (var stream = file.OpenReadStream())
        {
            await _storage.UploadAsync(newBlobName, stream, file.ContentType);
        }

        resume.FileName = Path.GetFileName(file.FileName);
        resume.BlobName = newBlobName;
        resume.ContentType = file.ContentType;
        resume.SizeBytes = file.Length;
        await _context.SaveChangesAsync();

        // The new file is safely recorded; now remove the replaced blob.
        if (oldBlobName != null && oldBlobName != newBlobName)
        {
            await _storage.DeleteAsync(oldBlobName);
        }

        return RedirectToAction(nameof(Index));
    }

    // GET: Resumes/Download/5 — stream the stored PDF back to its owner.
    public async Task<IActionResult> Download(int id)
    {
        var resume = await FindMineAsync(id);
        if (resume == null || resume.BlobName == null)
        {
            return NotFound();
        }

        var stream = await _storage.DownloadAsync(resume.BlobName);
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, resume.ContentType ?? "application/pdf", resume.FileName ?? "resume.pdf");
    }

    // POST: Resumes/DeleteFile/5 — remove just the file, keep the resume version.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFile(int id)
    {
        var resume = await FindMineAsync(id);
        if (resume == null)
        {
            return NotFound();
        }

        if (resume.BlobName != null)
        {
            await _storage.DeleteAsync(resume.BlobName);
        }

        resume.FileName = null;
        resume.BlobName = null;
        resume.ContentType = null;
        resume.SizeBytes = null;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
