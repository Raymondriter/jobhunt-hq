using System.Globalization;
using System.Security.Claims;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

[Authorize]
public class ImportController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;

    public ImportController(ApplicationDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET: Import
    public IActionResult Index()
    {
        return View();
    }

    // POST: Import/LoadDemoSeed — one-click import of the bundled fictional
    // 4-row demo seed CSV (idempotent, just like uploading the file).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadDemoSeed()
    {
        var seedPath = Path.Combine(_env.ContentRootPath, "seed", "applications-seed.csv");
        if (!System.IO.File.Exists(seedPath))
        {
            TempData["ErrorMessage"] = "Sample demo CSV was not found on the server.";
            return RedirectToAction(nameof(Index));
        }

        var text = await System.IO.File.ReadAllTextAsync(seedPath);
        var (result, error) = await ImportCsvTextAsync(text);
        if (error is not null)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Index));
        }

        TempData["StatusMessage"] = result.ImportedCount > 0
            ? $"Loaded {result.ImportedCount} sample demo application{(result.ImportedCount == 1 ? "" : "s")}."
            : $"All {result.DuplicateCount} sample demo applications are already in your tracker.";
        return View("Result", result);
    }

    // POST: Import
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Index(IFormFile? csvFile)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose a .csv file to import.");
            return View();
        }

        if (!Path.GetExtension(csvFile.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "The file must be a .csv file.");
            return View();
        }

        string text;
        using (var reader = new StreamReader(csvFile.OpenReadStream()))
        {
            text = await reader.ReadToEndAsync();
        }

        var (result, error) = await ImportCsvTextAsync(text);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View();
        }

        return View("Result", result);
    }

    private async Task<(ImportResultViewModel Result, string? Error)> ImportCsvTextAsync(string text)
    {
        var records = CsvParser.Parse(text);
        var result = new ImportResultViewModel();

        if (records.Count == 0)
        {
            return (result, "The file is empty — no header row found.");
        }

        // Map header names (case-insensitive) to column indexes.
        var header = records[0];
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Fields.Count; i++)
        {
            var name = header.Fields[i].Trim();
            if (name.Length > 0 && !columns.ContainsKey(name))
            {
                columns[name] = i;
            }
        }

        if (!columns.ContainsKey("Company") || !(columns.ContainsKey("Title") || columns.ContainsKey("RoleTitle")))
        {
            return (result, "The CSV needs at least a Company column and a Title (or RoleTitle) column in its header row.");
        }

        string? Get(CsvParser.CsvRecord row, params string[] names)
        {
            foreach (var name in names)
            {
                if (columns.TryGetValue(name, out var idx) && idx < row.Fields.Count)
                {
                    var value = row.Fields[idx].Trim();
                    if (value.Length > 0)
                    {
                        return value;
                    }
                }
            }
            return null;
        }

        // The current user's resume versions, matched by name for the ResumeName column.
        var myResumes = await _context.ResumeVersions
            .Where(r => r.UserId == CurrentUserId)
            .ToListAsync();
        var resumeByName = myResumes
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.UtcNow;
        var toAdd = new List<JobApplication>();

        // Existing applications for this user, keyed for idempotent re-imports
        // so importing the same CSV twice adds zero duplicate rows while
        // preserving genuinely separate postings.
        var existingApps = await _context.JobApplications
            .Where(a => a.UserId == CurrentUserId)
            .Select(a => new { a.Company, a.RoleTitle, a.DateApplied, a.Url })
            .ToListAsync();

        static string DedupKey(string company, string title, DateOnly? dateApplied, string? url) =>
            $"{company.Trim().ToLowerInvariant()}|{title.Trim().ToLowerInvariant()}|{dateApplied?.ToString("yyyy-MM-dd") ?? ""}|{(url ?? "").Trim().ToLowerInvariant()}";

        var seenKeys = new HashSet<string>(
            existingApps.Select(a => DedupKey(a.Company, a.RoleTitle, a.DateApplied, a.Url)),
            StringComparer.Ordinal);

        foreach (var row in records.Skip(1))
        {
            // A row of nothing but separators/whitespace is not a data row.
            if (row.Fields.All(f => string.IsNullOrWhiteSpace(f)))
            {
                continue;
            }

            var company = Get(row, "Company");
            var title = Get(row, "Title", "RoleTitle");

            if (company == null)
            {
                result.Skipped.Add(new ImportSkippedRow { LineNumber = row.LineNumber, Reason = "Missing Company." });
                continue;
            }
            if (title == null)
            {
                result.Skipped.Add(new ImportSkippedRow { LineNumber = row.LineNumber, Reason = "Missing Title." });
                continue;
            }
            if (company.Length > 200 || title.Length > 200)
            {
                result.Skipped.Add(new ImportSkippedRow { LineNumber = row.LineNumber, Reason = "Company or Title is longer than 200 characters." });
                continue;
            }

            // Status: match the enum case-insensitively; anything else defaults
            // to Saved and is reported as a warning (the row still imports).
            var status = ApplicationStatus.Saved;
            var statusText = Get(row, "Status");
            if (statusText != null && !Enum.TryParse<ApplicationStatus>(statusText, ignoreCase: true, out status))
            {
                status = ApplicationStatus.Saved;
                result.Warnings.Add($"Line {row.LineNumber}: status \"{statusText}\" was not recognized — imported as Saved.");
            }

            DateOnly? ParseDate(string? value, string columnName)
            {
                if (value == null)
                {
                    return null;
                }
                if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
                {
                    return iso;
                }
                if (DateOnly.TryParseExact(value, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var us))
                {
                    return us;
                }
                result.Warnings.Add($"Line {row.LineNumber}: {columnName} \"{value}\" was not a recognized date (yyyy-MM-dd or M/d/yyyy) — left blank.");
                return null;
            }

            int? ParseSalary(string? value, string columnName)
            {
                if (value == null)
                {
                    return null;
                }
                // Tolerate currency formatting from spreadsheet exports.
                var cleaned = value.Replace("$", "").Replace(",", "").Trim();
                if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                {
                    return amount;
                }
                result.Warnings.Add($"Line {row.LineNumber}: {columnName} \"{value}\" was not a whole number — left blank.");
                return null;
            }

            var dateApplied = ParseDate(Get(row, "DateApplied"), "DateApplied");
            var rowUrl = Truncate(Get(row, "Url"), 1000);
            var rowKey = DedupKey(company, title, dateApplied, rowUrl);
            if (!seenKeys.Add(rowKey))
            {
                result.DuplicateCount++;
                continue;
            }

            int? resumeVersionId = null;
            var resumeName = Get(row, "ResumeName");
            if (resumeName != null)
            {
                if (resumeByName.TryGetValue(resumeName, out var matchedId))
                {
                    resumeVersionId = matchedId;
                }
                else
                {
                    result.Warnings.Add($"Line {row.LineNumber}: resume \"{resumeName}\" did not match any of your resume versions — left unlinked.");
                }
            }

            var salaryMin = ParseSalary(Get(row, "SalaryMin"), "SalaryMin");
            var salaryMax = ParseSalary(Get(row, "SalaryMax"), "SalaryMax");

            toAdd.Add(new JobApplication
            {
                UserId = CurrentUserId,
                Company = company,
                RoleTitle = title,
                Url = rowUrl,
                PostingUrl = rowUrl,
                Location = Truncate(Get(row, "Location"), 200),
                Status = status,
                // Tracker exports rarely carry a separate "saved" date; the
                // applied date is the best record of when the row appeared.
                DateSaved = dateApplied ?? today,
                DateApplied = dateApplied,
                NextAction = Truncate(Get(row, "NextAction"), 500),
                NextActionDue = ParseDate(Get(row, "NextActionDue"), "NextActionDue"),
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                PayMin = salaryMin,
                PayMax = salaryMax,
                ResumeVersionId = resumeVersionId,
                Notes = Truncate(Get(row, "Notes"), 4000),
                CreatedUtc = now,
                UpdatedUtc = now
            });
        }

        if (toAdd.Count > 0)
        {
            _context.JobApplications.AddRange(toAdd);
            await _context.SaveChangesAsync();
        }

        result.ImportedCount = toAdd.Count;
        return (result, null);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value == null || value.Length <= maxLength ? value : value[..maxLength];
}
