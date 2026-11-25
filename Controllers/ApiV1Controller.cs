using System.Collections.Concurrent;
using System.Text;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

/// <summary>
/// Public REST API v1 (<c>docs/api-design.md</c>) for bring-your-own-AI clients.
/// Principle 1 (invariant): Read + prepare only — there is no submit endpoint,
/// ever. Authenticated via personal <c>Bearer jhq_…</c> tokens scoped per
/// capability and strictly isolated to the token owner's records.
/// </summary>
[ApiController]
[Route("api/v1")]
public class ApiV1Controller : ControllerBase
{
    private static readonly ConcurrentDictionary<string, RateBucket> RateBuckets = new();

    private readonly ApplicationDbContext _context;
    private readonly FitCheckService _fitCheck;
    private readonly AiPacketService _packet;
    private readonly InterviewPacketService _interviewPacket;
    private readonly PostingFetchService _postingFetch;
    private readonly ILogger<ApiV1Controller> _logger;

    public ApiV1Controller(
        ApplicationDbContext context,
        FitCheckService fitCheck,
        AiPacketService packet,
        InterviewPacketService interviewPacket,
        PostingFetchService postingFetch,
        ILogger<ApiV1Controller> logger)
    {
        _context = context;
        _fitCheck = fitCheck;
        _packet = packet;
        _interviewPacket = interviewPacket;
        _postingFetch = postingFetch;
        _logger = logger;
    }

    // ---------- Profile ----------

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var (token, err) = await AuthorizeTokenAsync("profile:read");
        if (err is not null) return err;

        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == token!.UserId);
        if (profile is null)
        {
            return ApiError(404, "not_found", "Search profile has not been created yet.");
        }

        return Ok(MapProfile(profile));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> PutProfile([FromBody] ProfileUpsertDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("profile:write");
        if (err is not null) return err;

        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == token!.UserId);
        if (profile is null)
        {
            profile = new SearchProfile
            {
                UserId = token!.UserId,
                CreatedUtc = DateTime.UtcNow
            };
            _context.SearchProfiles.Add(profile);
        }

        profile.TargetRoles = Truncate(dto.TargetRoles, 1000);
        profile.RemotePreference = Truncate(dto.RemotePreference, 200);
        profile.PayFloor = dto.PayFloor is >= 0 ? dto.PayFloor : null;
        profile.MustHaves = Truncate(dto.MustHaves, 2000);
        profile.DealBreakers = Truncate(dto.DealBreakers, 2000);
        profile.SkipCompanies = Truncate(dto.SkipCompanies, 2000);
        profile.StandingAnswers = dto.StandingAnswers?.Trim();
        profile.UpdatedUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(MapProfile(profile));
    }

    // ---------- Applications ----------

    [HttpGet("applications")]
    public async Task<IActionResult> ListApplications(
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? dueBefore = null,
        [FromQuery] int limit = 50)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:read");
        if (err is not null) return err;

        var query = _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == token!.UserId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ApplicationStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                return ApiError(400, "bad_request", $"Unknown status '{status}'.");
            }
            query = query.Where(a => a.Status == parsedStatus);
        }

        if (dueBefore.HasValue)
        {
            query = query.Where(a => a.NextActionDue != null && a.NextActionDue <= dueBefore.Value);
        }

        var clampedLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
        var items = await query
            .OrderByDescending(a => a.UpdatedUtc)
            .Take(clampedLimit)
            .ToListAsync();

        return Ok(new
        {
            items = items.Select(MapApplication),
            count = items.Count
        });
    }

    [HttpGet("applications/{id:int}")]
    public async Task<IActionResult> GetApplication(int id)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:read");
        if (err is not null) return err;

        var app = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == token!.UserId);
        if (app is null)
        {
            return ApiError(404, "not_found", "Application not found.");
        }

        return Ok(MapApplication(app));
    }

    [HttpPost("applications")]
    public async Task<IActionResult> CreateApplication([FromBody] ApplicationWriteDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        if (string.IsNullOrWhiteSpace(dto.Company) || string.IsNullOrWhiteSpace(dto.RoleTitle))
        {
            return ApiError(400, "bad_request", "Both company and roleTitle are required.");
        }

        var status = ApplicationStatus.Saved;
        if (!string.IsNullOrWhiteSpace(dto.Status) && !Enum.TryParse(dto.Status, ignoreCase: true, out status))
        {
            return ApiError(400, "bad_request", $"Unknown status '{dto.Status}'.");
        }

        if (dto.ResumeVersionId.HasValue)
        {
            var ownsResume = await _context.ResumeVersions
                .AnyAsync(r => r.Id == dto.ResumeVersionId.Value && r.UserId == token!.UserId);
            if (!ownsResume)
            {
                return ApiError(400, "bad_request", "resumeVersionId must belong to your account.");
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var effectiveUrl = !string.IsNullOrWhiteSpace(dto.PostingUrl) ? dto.PostingUrl : dto.Url;
        var effectivePayMin = dto.PayMin ?? dto.SalaryMin;
        var effectivePayMax = dto.PayMax ?? dto.SalaryMax;

        var app = new JobApplication
        {
            UserId = token!.UserId,
            Company = Truncate(dto.Company, 200)!,
            RoleTitle = Truncate(dto.RoleTitle, 200)!,
            Url = Truncate(effectiveUrl, 1000),
            PostingUrl = Truncate(effectiveUrl, 1000),
            Location = Truncate(dto.Location, 200),
            Status = status,
            DateSaved = dto.DateSaved ?? today,
            DateApplied = dto.DateApplied ?? (status == ApplicationStatus.Applied ? today : null),
            NextAction = Truncate(dto.NextAction, 500),
            NextActionDue = dto.NextActionDue,
            SalaryMin = effectivePayMin,
            SalaryMax = effectivePayMax,
            PayMin = effectivePayMin,
            PayMax = effectivePayMax,
            ResumeVersionId = dto.ResumeVersionId,
            Notes = Truncate(dto.Notes, 4000),
            PostingText = dto.PostingText?.Trim(),
            ConfirmationReference = Truncate(dto.ConfirmationReference, 200),
            BlockingQuestion = Truncate(dto.BlockingQuestion, 1000),
            ElevatorPitch = Truncate(dto.ElevatorPitch, 2000),
            CompanyResearch = Truncate(dto.CompanyResearch, 4000),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await RunAndApplyFitAsync(app, token.UserId);
        _context.JobApplications.Add(app);
        await _context.SaveChangesAsync();

        return StatusCode(201, MapApplication(app));
    }

    [HttpPatch("applications/{id:int}")]
    public async Task<IActionResult> PatchApplication(int id, [FromBody] ApplicationPatchDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        var app = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == token!.UserId);
        if (app is null)
        {
            return ApiError(404, "not_found", "Application not found.");
        }

        if (dto.Company is not null)
        {
            if (string.IsNullOrWhiteSpace(dto.Company))
                return ApiError(400, "bad_request", "company cannot be empty.");
            app.Company = Truncate(dto.Company, 200)!;
        }
        if (dto.RoleTitle is not null)
        {
            if (string.IsNullOrWhiteSpace(dto.RoleTitle))
                return ApiError(400, "bad_request", "roleTitle cannot be empty.");
            app.RoleTitle = Truncate(dto.RoleTitle, 200)!;
        }
        if (dto.Status is not null)
        {
            if (!Enum.TryParse<ApplicationStatus>(dto.Status, ignoreCase: true, out var parsedStatus))
                return ApiError(400, "bad_request", $"Unknown status '{dto.Status}'.");
            app.Status = parsedStatus;
            if (parsedStatus == ApplicationStatus.Applied && !app.DateApplied.HasValue)
            {
                app.DateApplied = DateOnly.FromDateTime(DateTime.Today);
            }
        }
        if (dto.Url is not null || dto.PostingUrl is not null)
        {
            var url = Truncate(dto.PostingUrl ?? dto.Url, 1000);
            app.Url = url;
            app.PostingUrl = url;
        }
        if (dto.Location is not null) app.Location = Truncate(dto.Location, 200);
        if (dto.DateApplied.HasValue) app.DateApplied = dto.DateApplied;
        if (dto.NextAction is not null) app.NextAction = Truncate(dto.NextAction, 500);
        if (dto.NextActionDue.HasValue) app.NextActionDue = dto.NextActionDue;
        if (dto.PayMin.HasValue) { app.PayMin = dto.PayMin; app.SalaryMin = dto.PayMin; }
        if (dto.PayMax.HasValue) { app.PayMax = dto.PayMax; app.SalaryMax = dto.PayMax; }
        if (dto.Notes is not null) app.Notes = Truncate(dto.Notes, 4000);
        if (dto.PostingText is not null) app.PostingText = dto.PostingText.Trim();
        if (dto.ConfirmationReference is not null) app.ConfirmationReference = Truncate(dto.ConfirmationReference, 200);
        if (dto.BlockingQuestion is not null) app.BlockingQuestion = Truncate(dto.BlockingQuestion, 1000);
        if (dto.ElevatorPitch is not null) app.ElevatorPitch = Truncate(dto.ElevatorPitch, 2000);
        if (dto.CompanyResearch is not null) app.CompanyResearch = Truncate(dto.CompanyResearch, 4000);

        await RunAndApplyFitAsync(app, token!.UserId);
        app.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(MapApplication(app));
    }

    [HttpPost("applications/{id:int}/move")]
    public async Task<IActionResult> MoveApplication(int id, [FromBody] MoveRequestDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        if (string.IsNullOrWhiteSpace(dto.Status) || !Enum.TryParse<ApplicationStatus>(dto.Status, ignoreCase: true, out var status))
        {
            return ApiError(400, "bad_request", "Valid status is required.");
        }

        var app = await _context.JobApplications
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == token!.UserId);
        if (app is null)
        {
            return ApiError(404, "not_found", "Application not found.");
        }

        app.Status = status;
        if (status == ApplicationStatus.Applied && !app.DateApplied.HasValue)
        {
            app.DateApplied = DateOnly.FromDateTime(DateTime.Today);
        }
        app.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = app.Id,
            status = app.Status.ToString(),
            dateApplied = app.DateApplied?.ToString("yyyy-MM-dd"),
            updatedAt = app.UpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });
    }

    [HttpDelete("applications/{id:int}")]
    public async Task<IActionResult> DeleteApplication(int id)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        var app = await _context.JobApplications
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == token!.UserId);
        if (app is null)
        {
            return ApiError(404, "not_found", "Application not found.");
        }

        _context.JobApplications.Remove(app);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Postings & Fit Check ----------

    [HttpPost("postings")]
    public async Task<IActionResult> CreatePosting([FromBody] PostingCreateDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("postings:write", bucketCategory: "fetch", maxPer5Min: 20);
        if (err is not null) return err;

        var postingText = dto.PostingText;
        var roleTitle = dto.RoleTitle;

        if (string.IsNullOrWhiteSpace(postingText) && !string.IsNullOrWhiteSpace(dto.Url))
        {
            var fetched = await _postingFetch.FetchAsync(dto.Url);
            if (!fetched.Success)
            {
                return ApiError(400, "fetch_failed", PostingFetchService.FailureMessage);
            }
            postingText = fetched.Text;
            if (string.IsNullOrWhiteSpace(roleTitle) && !string.IsNullOrWhiteSpace(fetched.Title))
            {
                roleTitle = fetched.Title;
            }
        }

        if (string.IsNullOrWhiteSpace(dto.Company) || string.IsNullOrWhiteSpace(roleTitle))
        {
            return ApiError(400, "bad_request", "Both company and roleTitle are required.");
        }

        var app = new JobApplication
        {
            UserId = token!.UserId,
            Company = Truncate(dto.Company, 200)!,
            RoleTitle = Truncate(roleTitle, 200)!,
            Url = Truncate(dto.Url, 1000),
            PostingUrl = Truncate(dto.Url, 1000),
            Location = Truncate(dto.Location, 200),
            PostingText = postingText?.Trim(),
            PayMin = dto.PayMin,
            PayMax = dto.PayMax,
            SalaryMin = dto.PayMin,
            SalaryMax = dto.PayMax,
            Status = ApplicationStatus.Saved,
            DateSaved = DateOnly.FromDateTime(DateTime.Today),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == token.UserId);
        var fit = _fitCheck.Evaluate(app, profile);
        app.FitVerdict = fit.Verdict;
        app.FitReasons = fit.ReasonsText;

        _context.JobApplications.Add(app);
        await _context.SaveChangesAsync();

        return StatusCode(201, MapApplication(app));
    }

    [HttpPost("fit-check")]
    public async Task<IActionResult> RunAdHocFitCheck([FromBody] AdHocFitCheckDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("fitcheck:run");
        if (err is not null) return err;

        var tempApp = new JobApplication
        {
            Company = dto.Company ?? string.Empty,
            RoleTitle = dto.RoleTitle ?? string.Empty,
            PostingText = dto.PostingText,
            PayMin = dto.PayMin,
            PayMax = dto.PayMax
        };

        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == token!.UserId);
        var result = _fitCheck.Evaluate(tempApp, profile);

        return Ok(new
        {
            verdict = result.Verdict,
            score = result.Score,
            blocked = result.Blocked,
            reasons = result.Reasons
        });
    }

    [HttpPost("applications/{id:int}/fit-check")]
    public async Task<IActionResult> RecheckApplicationFit(int id)
    {
        var (token, err) = await AuthorizeTokenAsync("fitcheck:run");
        if (err is not null) return err;

        var app = await _context.JobApplications
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == token!.UserId);
        if (app is null)
        {
            return ApiError(404, "not_found", "Application not found.");
        }

        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == token!.UserId);
        var result = _fitCheck.Evaluate(app, profile);
        app.FitVerdict = result.Verdict;
        app.FitReasons = result.ReasonsText;
        app.UpdatedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = app.Id,
            verdict = result.Verdict,
            score = result.Score,
            blocked = result.Blocked,
            reasons = result.Reasons
        });
    }

    // ---------- Packets & Export ----------

    [HttpGet("applications/{id:int}/packet")]
    public async Task<IActionResult> GetApplicationPacket(int id, [FromQuery] string format = "json")
    {
        var (token, err) = await AuthorizeTokenAsync("packets:generate", bucketCategory: "packets", maxPer5Min: 30);
        if (err is not null) return err;

        var app = await _context.JobApplications
            .Include(a => a.ResumeVersion)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == token!.UserId);
        if (app is null)
        {
            return ApiError(404, "not_found", "Application not found.");
        }

        var userId = token!.UserId;
        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var resume = app.ResumeVersion ?? await _context.ResumeVersions
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefaultAsync();
        var otherCount = await _context.JobApplications
            .CountAsync(a => a.UserId == userId && a.Id != app.Id && a.Company == app.Company);

        var packet = await _packet.BuildPacketAsync(app, profile, resume, otherCount);
        if (string.Equals(format, "md", StringComparison.OrdinalIgnoreCase))
        {
            return Content(_packet.BuildMarkdown(packet), "text/markdown; charset=utf-8");
        }
        return Content(_packet.BuildJson(packet), "application/json; charset=utf-8");
    }

    [HttpGet("applications/{id:int}/interview-packet")]
    public async Task<IActionResult> GetInterviewPacket(int id, [FromQuery] string format = "json", [FromQuery] int? roundId = null)
    {
        var (token, err) = await AuthorizeTokenAsync("packets:generate", bucketCategory: "packets", maxPer5Min: 30);
        if (err is not null) return err;

        var app = await _context.JobApplications
            .Include(a => a.ResumeVersion)
            .Include(a => a.InterviewRounds)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == token!.UserId);
        if (app is null)
        {
            return ApiError(404, "not_found", "Application not found.");
        }

        var userId = token!.UserId;
        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var resume = app.ResumeVersion ?? await _context.ResumeVersions
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefaultAsync();
        var rounds = app.InterviewRounds
            .OrderBy(r => r.RoundNumber)
            .ThenBy(r => r.ScheduledLocal)
            .ToList();
        var focus = roundId.HasValue
            ? rounds.FirstOrDefault(r => r.Id == roundId.Value)
            : rounds.FirstOrDefault(r => !r.Completed) ?? rounds.LastOrDefault();
        var stories = await _context.StoryBankItems
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedUtc)
            .ToListAsync();

        var packet = await _interviewPacket.BuildPacketAsync(app, profile, resume, rounds, focus, stories);
        if (string.Equals(format, "md", StringComparison.OrdinalIgnoreCase))
        {
            return Content(_interviewPacket.BuildMarkdown(packet), "text/markdown; charset=utf-8");
        }
        return Content(_interviewPacket.BuildJson(packet), "application/json; charset=utf-8");
    }

    [HttpGet("export")]
    public async Task<IActionResult> GetExport()
    {
        var (token, err) = await AuthorizeTokenAsync("applications:read");
        if (err is not null) return err;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == token!.UserId);
        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == token!.UserId);
        var apps = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == token!.UserId)
            .OrderBy(a => a.Id)
            .ToListAsync();
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == token!.UserId)
            .OrderBy(r => r.Id)
            .ToListAsync();
        var stories = await _context.StoryBankItems
            .Where(s => s.UserId == token!.UserId)
            .OrderBy(s => s.Id)
            .ToListAsync();

        var export = new UserDataExport
        {
            ExportedAtUtc = DateTime.UtcNow,
            AccountEmail = user?.Email ?? string.Empty,
            SearchProfile = profile is null ? null : SearchProfileExport.FromEntity(profile),
            Applications = apps.Select(ApplicationExport.FromEntity).ToList(),
            ResumeVersions = resumes.Select(ResumeVersionExport.FromEntity).ToList(),
            StoryBank = stories.Select(StoryBankExport.FromEntity).ToList()
        };

        return Ok(export);
    }

    // ---------- Muse & External Agent Centralization Sync (PRODUCT.md) ----------

    /// <summary>
    /// Idempotent single-attempt handoff (<c>PRODUCT.md</c> "First product experiment"):
    /// lets Muse (or a Gmail/tracker sync script) report an application preparation
    /// or assisted-submission outcome with a stable <c>attemptId</c>.
    /// - Confirmed attempts become <c>Applied</c> (without downgrading active Interviews/Offers).
    /// - Interrupted attempts stay <c>Saved</c> and surface <c>blockingQuestion</c> without counting as Applied.
    /// - Replaying the same payload updates the existing record with zero duplicates.
    /// </summary>
    [HttpPost("sync/outcome")]
    public async Task<IActionResult> SyncOutcome([FromBody] SyncOutcomeDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        var userId = token!.UserId;
        var existingApps = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == userId)
            .ToListAsync();
        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        var (outcome, errorMsg) = UpsertSingleOutcome(dto, userId, existingApps, profile);
        if (errorMsg is not null)
        {
            return ApiError(400, "bad_request", errorMsg);
        }

        await _context.SaveChangesAsync();
        return Ok(new
        {
            created = outcome!.Value.Created,
            updated = outcome.Value.Updated,
            application = MapApplication(outcome.Value.Application)
        });
    }

    /// <summary>
    /// Idempotent batch sync for Muse scan batches, historical tracker imports,
    /// or Gmail confirmation sweeps. Replaying the same batch adds zero duplicates.
    /// </summary>
    [HttpPost("sync/batch")]
    public async Task<IActionResult> SyncBatch([FromBody] SyncBatchDto batch)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        if (batch.Items is null || batch.Items.Count == 0)
        {
            return ApiError(400, "bad_request", "items array must contain at least one entry.");
        }
        if (batch.Items.Count > 200)
        {
            return ApiError(400, "bad_request", "Maximum 200 items per sync batch.");
        }

        var userId = token!.UserId;
        var workingApps = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == userId)
            .ToListAsync();
        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        var createdCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        var errors = new List<string>();

        for (var i = 0; i < batch.Items.Count; i++)
        {
            var (res, itemErr) = UpsertSingleOutcome(batch.Items[i], userId, workingApps, profile);
            if (itemErr is not null)
            {
                errors.Add($"Item {i}: {itemErr}");
                continue;
            }

            if (res!.Value.Created)
            {
                createdCount++;
                workingApps.Add(res.Value.Application);
            }
            else if (res.Value.Updated)
            {
                updatedCount++;
            }
            else
            {
                unchangedCount++;
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            createdCount,
            updatedCount,
            unchangedCount,
            totalTracked = workingApps.Count,
            errors
        });
    }

    // ---------- Story Bank & Full Workspace Sync ----------

    [HttpGet("stories")]
    public async Task<IActionResult> ListStories()
    {
        var (token, err) = await AuthorizeTokenAsync("applications:read");
        if (err is not null) return err;

        var items = await _context.StoryBankItems
            .Where(s => s.UserId == token!.UserId)
            .OrderByDescending(s => s.UpdatedUtc)
            .ToListAsync();

        return Ok(new
        {
            items = items.Select(MapStory),
            count = items.Count
        });
    }

    [HttpPost("stories")]
    public async Task<IActionResult> UpsertStory([FromBody] StorySyncDto dto)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return ApiError(400, "bad_request", "title is required.");
        }

        var userId = token!.UserId;
        var existingStories = await _context.StoryBankItems
            .Where(s => s.UserId == userId)
            .ToListAsync();

        var (story, created, _) = UpsertSingleStory(dto, userId, existingStories);
        await _context.SaveChangesAsync();

        return StatusCode(created ? 201 : 200, MapStory(story));
    }

    /// <summary>
    /// Idempotent full-workspace sync for bootstrapping or keeping Search Profile,
    /// Applications (including Interview Rounds), and STAR Story Bank aligned
    /// across environments or external agents.
    /// </summary>
    [HttpPost("sync/workspace")]
    public async Task<IActionResult> SyncWorkspace([FromBody] SyncWorkspaceDto workspace)
    {
        var (token, err) = await AuthorizeTokenAsync("applications:write");
        if (err is not null) return err;

        if (workspace.Profile is not null && !token!.HasScope("profile:write"))
        {
            return ApiError(403, "forbidden", "Token lacks required scope 'profile:write' for profile sync.");
        }

        var userId = token!.UserId;
        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var profileUpdated = false;

        if (workspace.Profile is not null)
        {
            if (profile is null)
            {
                profile = new SearchProfile
                {
                    UserId = userId,
                    CreatedUtc = DateTime.UtcNow
                };
                _context.SearchProfiles.Add(profile);
            }

            profile.TargetRoles = Truncate(workspace.Profile.TargetRoles, 1000);
            profile.RemotePreference = Truncate(workspace.Profile.RemotePreference, 200);
            profile.PayFloor = workspace.Profile.PayFloor is >= 0 ? workspace.Profile.PayFloor : null;
            profile.MustHaves = Truncate(workspace.Profile.MustHaves, 2000);
            profile.DealBreakers = Truncate(workspace.Profile.DealBreakers, 2000);
            profile.SkipCompanies = Truncate(workspace.Profile.SkipCompanies, 2000);
            profile.StandingAnswers = workspace.Profile.StandingAnswers?.Trim();
            profile.UpdatedUtc = DateTime.UtcNow;
            profileUpdated = true;
        }

        var workingApps = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == userId)
            .ToListAsync();

        var appsCreated = 0;
        var appsUpdated = 0;
        var appsUnchanged = 0;
        var errors = new List<string>();

        if (workspace.Items is { Count: > 0 })
        {
            if (workspace.Items.Count > 200)
            {
                return ApiError(400, "bad_request", "Maximum 200 items per sync workspace call.");
            }

            for (var i = 0; i < workspace.Items.Count; i++)
            {
                var (res, itemErr) = UpsertSingleOutcome(workspace.Items[i], userId, workingApps, profile);
                if (itemErr is not null)
                {
                    errors.Add($"Item {i}: {itemErr}");
                    continue;
                }

                if (res!.Value.Created)
                {
                    appsCreated++;
                    workingApps.Add(res.Value.Application);
                }
                else if (res.Value.Updated)
                {
                    appsUpdated++;
                }
                else
                {
                    appsUnchanged++;
                }
            }
        }

        var workingStories = await _context.StoryBankItems
            .Where(s => s.UserId == userId)
            .ToListAsync();
        var storiesCreated = 0;
        var storiesUpdated = 0;

        if (workspace.Stories is { Count: > 0 })
        {
            foreach (var sDto in workspace.Stories)
            {
                if (string.IsNullOrWhiteSpace(sDto.Title)) continue;
                var (story, sCreated, sUpdated) = UpsertSingleStory(sDto, userId, workingStories);
                if (sCreated)
                {
                    storiesCreated++;
                    workingStories.Add(story);
                }
                else if (sUpdated)
                {
                    storiesUpdated++;
                }
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            profileUpdated,
            applications = new
            {
                createdCount = appsCreated,
                updatedCount = appsUpdated,
                unchangedCount = appsUnchanged,
                totalTracked = workingApps.Count
            },
            stories = new
            {
                createdCount = storiesCreated,
                updatedCount = storiesUpdated,
                totalStories = workingStories.Count
            },
            errors
        });
    }

    private (StoryBankItem Story, bool Created, bool Updated) UpsertSingleStory(
        StorySyncDto dto,
        string userId,
        List<StoryBankItem> existingStories)
    {
        var title = Truncate(dto.Title, 200)!;
        var match = existingStories.FirstOrDefault(s =>
            string.Equals(s.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var created = new StoryBankItem
            {
                UserId = userId,
                Title = title,
                Tags = Truncate(dto.Tags, 500),
                Situation = Truncate(dto.Situation, 2000),
                Task = Truncate(dto.Task, 2000),
                Action = Truncate(dto.Action, 3000),
                Result = Truncate(dto.Result, 2000),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            _context.StoryBankItems.Add(created);
            return (created, true, false);
        }

        var updated = false;
        if (!string.IsNullOrWhiteSpace(dto.Tags) && match.Tags != Truncate(dto.Tags, 500))
        {
            match.Tags = Truncate(dto.Tags, 500);
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.Situation) && match.Situation != Truncate(dto.Situation, 2000))
        {
            match.Situation = Truncate(dto.Situation, 2000);
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.Task) && match.Task != Truncate(dto.Task, 2000))
        {
            match.Task = Truncate(dto.Task, 2000);
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.Action) && match.Action != Truncate(dto.Action, 3000))
        {
            match.Action = Truncate(dto.Action, 3000);
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.Result) && match.Result != Truncate(dto.Result, 2000))
        {
            match.Result = Truncate(dto.Result, 2000);
            updated = true;
        }
        if (updated)
        {
            match.UpdatedUtc = DateTime.UtcNow;
        }
        return (match, false, updated);
    }

    private ((JobApplication Application, bool Created, bool Updated)? Result, string? Error) UpsertSingleOutcome(
        SyncOutcomeDto dto,
        string userId,
        List<JobApplication> existingApps,
        SearchProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(dto.Company) || string.IsNullOrWhiteSpace(dto.RoleTitle))
        {
            return (null, "Both company and roleTitle are required.");
        }

        var attemptId = Truncate(dto.AttemptId, 120);
        var company = Truncate(dto.Company, 200)!;
        var roleTitle = Truncate(dto.RoleTitle, 200)!;
        var url = Truncate(dto.Url, 1000);

        // Determine target status from either Outcome ("confirmed" | "interrupted" | "saved" | "interview" | "rejected")
        // or explicit Status.
        ApplicationStatus targetStatus;
        var outcomeNorm = dto.Outcome?.Trim().ToLowerInvariant();
        if (outcomeNorm is "interrupted" or "blocked" or "needs_input" or "saved")
        {
            targetStatus = ApplicationStatus.Saved;
        }
        else if (outcomeNorm is "confirmed" or "applied" or "submitted")
        {
            targetStatus = ApplicationStatus.Applied;
        }
        else if (outcomeNorm == "screening")
        {
            targetStatus = ApplicationStatus.Screening;
        }
        else if (outcomeNorm == "interview")
        {
            targetStatus = ApplicationStatus.Interview;
        }
        else if (outcomeNorm == "offer")
        {
            targetStatus = ApplicationStatus.Offer;
        }
        else if (outcomeNorm == "rejected")
        {
            targetStatus = ApplicationStatus.Rejected;
        }
        else if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            if (!Enum.TryParse(dto.Status, ignoreCase: true, out targetStatus))
            {
                return (null, $"Unknown status '{dto.Status}'.");
            }
        }
        else
        {
            targetStatus = !string.IsNullOrWhiteSpace(dto.BlockingQuestion) && string.IsNullOrWhiteSpace(dto.ConfirmationReference)
                ? ApplicationStatus.Saved
                : ApplicationStatus.Applied;
        }

        // Match idempotently:
        // 1. Exact ExternalAttemptId match (when supplied)
        // 2. Same Company + RoleTitle (case-insensitive) where URL matches or DateApplied matches or only one open match exists
        JobApplication? match = null;
        if (!string.IsNullOrWhiteSpace(attemptId))
        {
            match = existingApps.FirstOrDefault(a =>
                string.Equals(a.ExternalAttemptId, attemptId, StringComparison.OrdinalIgnoreCase));
        }
        if (match is null)
        {
            match = existingApps.FirstOrDefault(a =>
                string.Equals(a.Company.Trim(), company, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.RoleTitle.Trim(), roleTitle, StringComparison.OrdinalIgnoreCase)
                && (dto.DateApplied == null || a.DateApplied == null || a.DateApplied == dto.DateApplied));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var created = false;
        var updated = false;

        if (match is null)
        {
            created = true;
            match = new JobApplication
            {
                UserId = userId,
                Company = company,
                RoleTitle = roleTitle,
                Url = url,
                PostingUrl = url,
                Location = Truncate(dto.Location, 200),
                Status = targetStatus,
                DateSaved = dto.DateApplied ?? today,
                DateApplied = targetStatus == ApplicationStatus.Saved ? null : (dto.DateApplied ?? today),
                NextAction = Truncate(dto.NextAction, 500),
                NextActionDue = dto.NextActionDue,
                PayMin = dto.PayMin,
                PayMax = dto.PayMax,
                SalaryMin = dto.PayMin,
                SalaryMax = dto.PayMax,
                PostingText = dto.PostingText?.Trim(),
                Notes = Truncate(dto.Notes, 4000),
                ConfirmationReference = Truncate(dto.ConfirmationReference, 200),
                BlockingQuestion = targetStatus == ApplicationStatus.Saved ? Truncate(dto.BlockingQuestion, 1000) : null,
                ElevatorPitch = Truncate(dto.ElevatorPitch, 2000),
                CompanyResearch = Truncate(dto.CompanyResearch, 4000),
                ExternalAttemptId = attemptId,
                SourceAgent = Truncate(dto.SourceAgent ?? "Muse", 60),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(match.PostingText) || match.PayMin.HasValue || match.PayMax.HasValue)
            {
                var fit = _fitCheck.Evaluate(match, profile);
                match.FitVerdict = fit.Verdict;
                match.FitReasons = fit.ReasonsText;
            }

            if (dto.InterviewRounds is { Count: > 0 })
            {
                foreach (var rDto in dto.InterviewRounds)
                {
                    match.InterviewRounds.Add(new InterviewRound
                    {
                        UserId = userId,
                        RoundNumber = Math.Clamp(rDto.RoundNumber <= 0 ? 1 : rDto.RoundNumber, 1, 20),
                        RoundType = Truncate(rDto.RoundType, 100) ?? "Recruiter Screen",
                        ScheduledLocal = rDto.ScheduledLocal,
                        TimeZoneLabel = Truncate(rDto.TimeZoneLabel, 60),
                        DurationMinutes = Math.Clamp(rDto.DurationMinutes <= 0 ? 45 : rDto.DurationMinutes, 10, 480),
                        MeetingUrl = Truncate(rDto.MeetingUrl, 1000),
                        Interviewers = Truncate(rDto.Interviewers, 500),
                        PrepNotes = Truncate(rDto.PrepNotes, 4000),
                        QuestionsToAsk = Truncate(rDto.QuestionsToAsk, 4000),
                        DebriefNotes = Truncate(rDto.DebriefNotes, 4000),
                        Completed = rDto.Completed,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    });
                }
            }

            _context.JobApplications.Add(match);
            return ((match, true, false), null);
        }

        // Update existing record idempotently without downgrading an application
        // that has already progressed further in the pipeline (e.g. Screening/Interview/Offer).
        bool ShouldAdvanceStatus(ApplicationStatus current, ApplicationStatus incoming)
        {
            if (current == incoming) return false;
            if (current is ApplicationStatus.Screening or ApplicationStatus.Interview or ApplicationStatus.Offer
                && incoming is ApplicationStatus.Saved or ApplicationStatus.Applied)
            {
                return false;
            }
            return true;
        }

        if (ShouldAdvanceStatus(match.Status, targetStatus))
        {
            match.Status = targetStatus;
            updated = true;
        }
        if (targetStatus != ApplicationStatus.Saved && match.DateApplied is null && dto.DateApplied.HasValue)
        {
            match.DateApplied = dto.DateApplied;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(attemptId) && match.ExternalAttemptId != attemptId)
        {
            match.ExternalAttemptId = attemptId;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.SourceAgent) && string.IsNullOrWhiteSpace(match.SourceAgent))
        {
            match.SourceAgent = Truncate(dto.SourceAgent, 60);
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(match.PostingUrl))
        {
            match.Url = url;
            match.PostingUrl = url;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.Location) && string.IsNullOrWhiteSpace(match.Location))
        {
            match.Location = Truncate(dto.Location, 200);
            updated = true;
        }
        if (dto.PayMin.HasValue && !match.PayMin.HasValue)
        {
            match.PayMin = dto.PayMin;
            match.SalaryMin = dto.PayMin;
            updated = true;
        }
        if (dto.PayMax.HasValue && !match.PayMax.HasValue)
        {
            match.PayMax = dto.PayMax;
            match.SalaryMax = dto.PayMax;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.ConfirmationReference) && match.ConfirmationReference != dto.ConfirmationReference.Trim())
        {
            match.ConfirmationReference = Truncate(dto.ConfirmationReference, 200);
            if (match.Status != ApplicationStatus.Saved)
            {
                match.BlockingQuestion = null;
            }
            updated = true;
        }
        if (targetStatus == ApplicationStatus.Saved && !string.IsNullOrWhiteSpace(dto.BlockingQuestion) && match.BlockingQuestion != dto.BlockingQuestion.Trim())
        {
            match.BlockingQuestion = Truncate(dto.BlockingQuestion, 1000);
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.PostingText) && string.IsNullOrWhiteSpace(match.PostingText))
        {
            match.PostingText = dto.PostingText.Trim();
            var fit = _fitCheck.Evaluate(match, profile);
            match.FitVerdict = fit.Verdict;
            match.FitReasons = fit.ReasonsText;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.NextAction) && string.IsNullOrWhiteSpace(match.NextAction))
        {
            match.NextAction = Truncate(dto.NextAction, 500);
            match.NextActionDue = dto.NextActionDue;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.ElevatorPitch) && match.ElevatorPitch != Truncate(dto.ElevatorPitch, 2000))
        {
            match.ElevatorPitch = Truncate(dto.ElevatorPitch, 2000);
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.CompanyResearch) && match.CompanyResearch != Truncate(dto.CompanyResearch, 4000))
        {
            match.CompanyResearch = Truncate(dto.CompanyResearch, 4000);
            updated = true;
        }
        if (dto.InterviewRounds is { Count: > 0 })
        {
            foreach (var rDto in dto.InterviewRounds)
            {
                var roundNum = Math.Clamp(rDto.RoundNumber <= 0 ? 1 : rDto.RoundNumber, 1, 20);
                var roundType = Truncate(rDto.RoundType, 100) ?? "Recruiter Screen";
                var existingRound = match.InterviewRounds.FirstOrDefault(r =>
                    r.RoundNumber == roundNum
                    && string.Equals(r.RoundType, roundType, StringComparison.OrdinalIgnoreCase));

                if (existingRound is null)
                {
                    match.InterviewRounds.Add(new InterviewRound
                    {
                        UserId = userId,
                        JobApplicationId = match.Id,
                        RoundNumber = roundNum,
                        RoundType = roundType,
                        ScheduledLocal = rDto.ScheduledLocal,
                        TimeZoneLabel = Truncate(rDto.TimeZoneLabel, 60),
                        DurationMinutes = Math.Clamp(rDto.DurationMinutes <= 0 ? 45 : rDto.DurationMinutes, 10, 480),
                        MeetingUrl = Truncate(rDto.MeetingUrl, 1000),
                        Interviewers = Truncate(rDto.Interviewers, 500),
                        PrepNotes = Truncate(rDto.PrepNotes, 4000),
                        QuestionsToAsk = Truncate(rDto.QuestionsToAsk, 4000),
                        DebriefNotes = Truncate(rDto.DebriefNotes, 4000),
                        Completed = rDto.Completed,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    });
                    updated = true;
                }
                else
                {
                    if (rDto.ScheduledLocal.HasValue && existingRound.ScheduledLocal != rDto.ScheduledLocal)
                    {
                        existingRound.ScheduledLocal = rDto.ScheduledLocal;
                        existingRound.UpdatedUtc = DateTime.UtcNow;
                        updated = true;
                    }
                    if (!string.IsNullOrWhiteSpace(rDto.MeetingUrl) && existingRound.MeetingUrl != Truncate(rDto.MeetingUrl, 1000))
                    {
                        existingRound.MeetingUrl = Truncate(rDto.MeetingUrl, 1000);
                        existingRound.UpdatedUtc = DateTime.UtcNow;
                        updated = true;
                    }
                    if (!string.IsNullOrWhiteSpace(rDto.Interviewers) && existingRound.Interviewers != Truncate(rDto.Interviewers, 500))
                    {
                        existingRound.Interviewers = Truncate(rDto.Interviewers, 500);
                        existingRound.UpdatedUtc = DateTime.UtcNow;
                        updated = true;
                    }
                    if (!string.IsNullOrWhiteSpace(rDto.PrepNotes) && string.IsNullOrWhiteSpace(existingRound.PrepNotes))
                    {
                        existingRound.PrepNotes = Truncate(rDto.PrepNotes, 4000);
                        existingRound.UpdatedUtc = DateTime.UtcNow;
                        updated = true;
                    }
                    if (!string.IsNullOrWhiteSpace(rDto.QuestionsToAsk) && string.IsNullOrWhiteSpace(existingRound.QuestionsToAsk))
                    {
                        existingRound.QuestionsToAsk = Truncate(rDto.QuestionsToAsk, 4000);
                        existingRound.UpdatedUtc = DateTime.UtcNow;
                        updated = true;
                    }
                }
            }
        }

        if (updated)
        {
            match.UpdatedUtc = DateTime.UtcNow;
        }

        return ((match, created, updated), null);
    }

    // ---------- Auth, Rate Limiting & Helpers ----------

    private async Task<(ApiToken? Token, IActionResult? Error)> AuthorizeTokenAsync(
        string requiredScope,
        string bucketCategory = "general",
        int maxPer5Min = 300)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return (null, ApiError(401, "unauthorized", "Missing Authorization: Bearer <token> header."));
        }

        var rawToken = authHeader["Bearer ".Length..].Trim();
        if (!rawToken.StartsWith(ApiToken.Prefix, StringComparison.Ordinal) || rawToken.Length < 20)
        {
            return (null, ApiError(401, "unauthorized", "Invalid API token."));
        }

        var hash = ApiToken.ComputeSha256Hex(rawToken);
        var token = await _context.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null || token.IsRevoked)
        {
            return (null, ApiError(401, "unauthorized", "Invalid or revoked API token."));
        }

        if (!token.HasScope(requiredScope))
        {
            _logger.LogInformation("API token {TokenId} denied missing scope {Scope}", token.Id, requiredScope);
            return (null, ApiError(403, "forbidden", $"Token lacks required scope '{requiredScope}'."));
        }

        // Per-user ceiling across all tokens (docs/api-design.md §4).
        if (!TryConsumeRateLimit($"{token.UserId}:general", 300, out var retrySeconds)
            || (bucketCategory != "general" && !TryConsumeRateLimit($"{token.UserId}:{bucketCategory}", maxPer5Min, out retrySeconds)))
        {
            Response.Headers["Retry-After"] = retrySeconds.ToString();
            return (null, ApiError(429, "rate_limited", "Rate limit exceeded. Check Retry-After and try again shortly."));
        }

        token.LastUsedUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("API call by token {TokenId} on {Path}", token.Id, Request.Path);

        return (token, null);
    }

    private static bool TryConsumeRateLimit(string key, int limit, out int retryAfterSeconds)
    {
        var now = DateTime.UtcNow;
        var window = TimeSpan.FromMinutes(5);
        var bucket = RateBuckets.GetOrAdd(key, _ => new RateBucket { WindowStartUtc = now, Count = 0 });

        lock (bucket)
        {
            if (now - bucket.WindowStartUtc >= window)
            {
                bucket.WindowStartUtc = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= limit)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((bucket.WindowStartUtc + window - now).TotalSeconds));
                return false;
            }

            bucket.Count++;
            retryAfterSeconds = 0;
            return true;
        }
    }

    private sealed class RateBucket
    {
        public DateTime WindowStartUtc { get; set; }
        public int Count { get; set; }
    }

    private async Task RunAndApplyFitAsync(JobApplication app, string userId)
    {
        var hasMaterial = !string.IsNullOrWhiteSpace(app.PostingText)
            || !string.IsNullOrWhiteSpace(app.PostingUrl)
            || app.PayMin.HasValue
            || app.PayMax.HasValue
            || app.FitVerdict is not null;
        if (!hasMaterial) return;

        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var result = _fitCheck.Evaluate(app, profile);
        app.FitVerdict = result.Verdict;
        app.FitReasons = result.ReasonsText;
    }

    private ObjectResult ApiError(int statusCode, string code, string message) =>
        StatusCode(statusCode, new { error = new { code, message } });

    private static object MapProfile(SearchProfile p) => new
    {
        targetRoles = p.TargetRoles,
        remotePreference = p.RemotePreference,
        payFloor = p.PayFloor,
        mustHaves = p.MustHaves,
        dealBreakers = p.DealBreakers,
        skipCompanies = p.SkipCompanies,
        standingAnswers = p.StandingAnswers,
        createdAt = p.CreatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        updatedAt = p.UpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
    };

    private static object MapApplication(JobApplication a) => new
    {
        id = a.Id,
        company = a.Company,
        roleTitle = a.RoleTitle,
        url = a.Url,
        location = a.Location,
        status = a.Status.ToString(),
        dateSaved = a.DateSaved.ToString("yyyy-MM-dd"),
        dateApplied = a.DateApplied?.ToString("yyyy-MM-dd"),
        nextAction = a.NextAction,
        nextActionDue = a.NextActionDue?.ToString("yyyy-MM-dd"),
        salaryMin = a.SalaryMin,
        salaryMax = a.SalaryMax,
        resumeVersionId = a.ResumeVersionId,
        notes = a.Notes,
        postingUrl = a.PostingUrl,
        postingText = a.PostingText,
        payMin = a.PayMin,
        payMax = a.PayMax,
        fitVerdict = a.FitVerdict,
        fitReasons = a.FitReasons,
        confirmationReference = a.ConfirmationReference,
        blockingQuestion = a.BlockingQuestion,
        externalAttemptId = a.ExternalAttemptId,
        sourceAgent = a.SourceAgent,
        elevatorPitch = a.ElevatorPitch,
        companyResearch = a.CompanyResearch,
        interviewRoundsCount = a.InterviewRounds?.Count ?? 0,
        createdAt = a.CreatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        updatedAt = a.UpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
    };

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    public sealed class ProfileUpsertDto
    {
        public string? TargetRoles { get; set; }
        public string? RemotePreference { get; set; }
        public int? PayFloor { get; set; }
        public string? MustHaves { get; set; }
        public string? DealBreakers { get; set; }
        public string? SkipCompanies { get; set; }
        public string? StandingAnswers { get; set; }
    }

    public sealed class ApplicationWriteDto
    {
        public string? Company { get; set; }
        public string? RoleTitle { get; set; }
        public string? Url { get; set; }
        public string? PostingUrl { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public DateOnly? DateSaved { get; set; }
        public DateOnly? DateApplied { get; set; }
        public string? NextAction { get; set; }
        public DateOnly? NextActionDue { get; set; }
        public int? SalaryMin { get; set; }
        public int? SalaryMax { get; set; }
        public int? PayMin { get; set; }
        public int? PayMax { get; set; }
        public int? ResumeVersionId { get; set; }
        public string? Notes { get; set; }
        public string? PostingText { get; set; }
        public string? ConfirmationReference { get; set; }
        public string? BlockingQuestion { get; set; }
        public string? ElevatorPitch { get; set; }
        public string? CompanyResearch { get; set; }
    }

    public sealed class ApplicationPatchDto
    {
        public string? Company { get; set; }
        public string? RoleTitle { get; set; }
        public string? Url { get; set; }
        public string? PostingUrl { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public DateOnly? DateApplied { get; set; }
        public string? NextAction { get; set; }
        public DateOnly? NextActionDue { get; set; }
        public int? PayMin { get; set; }
        public int? PayMax { get; set; }
        public string? Notes { get; set; }
        public string? PostingText { get; set; }
        public string? ConfirmationReference { get; set; }
        public string? BlockingQuestion { get; set; }
        public string? ElevatorPitch { get; set; }
        public string? CompanyResearch { get; set; }
    }

    public sealed class MoveRequestDto
    {
        public string? Status { get; set; }
    }

    public sealed class PostingCreateDto
    {
        public string? Company { get; set; }
        public string? RoleTitle { get; set; }
        public string? Url { get; set; }
        public string? Location { get; set; }
        public int? PayMin { get; set; }
        public int? PayMax { get; set; }
        public string? PostingText { get; set; }
    }

    public sealed class AdHocFitCheckDto
    {
        public string? Company { get; set; }
        public string? RoleTitle { get; set; }
        public string? PostingText { get; set; }
        public int? PayMin { get; set; }
        public int? PayMax { get; set; }
    }

    public sealed class SyncOutcomeDto
    {
        public string? AttemptId { get; set; }
        public string? SourceAgent { get; set; }
        public string? Outcome { get; set; }
        public string? Status { get; set; }
        public string? Company { get; set; }
        public string? RoleTitle { get; set; }
        public string? Url { get; set; }
        public string? Location { get; set; }
        public DateOnly? DateApplied { get; set; }
        public int? PayMin { get; set; }
        public int? PayMax { get; set; }
        public string? PostingText { get; set; }
        public string? ConfirmationReference { get; set; }
        public string? BlockingQuestion { get; set; }
        public string? NextAction { get; set; }
        public DateOnly? NextActionDue { get; set; }
        public string? Notes { get; set; }
        public string? ElevatorPitch { get; set; }
        public string? CompanyResearch { get; set; }
        public List<InterviewRoundSyncDto>? InterviewRounds { get; set; }
    }

    public sealed class InterviewRoundSyncDto
    {
        public int RoundNumber { get; set; } = 1;
        public string? RoundType { get; set; }
        public DateTime? ScheduledLocal { get; set; }
        public string? TimeZoneLabel { get; set; }
        public int DurationMinutes { get; set; } = 45;
        public string? MeetingUrl { get; set; }
        public string? Interviewers { get; set; }
        public string? PrepNotes { get; set; }
        public string? QuestionsToAsk { get; set; }
        public string? DebriefNotes { get; set; }
        public bool Completed { get; set; }
    }

    public sealed class StorySyncDto
    {
        public string? Title { get; set; }
        public string? Tags { get; set; }
        public string? Situation { get; set; }
        public string? Task { get; set; }
        public string? Action { get; set; }
        public string? Result { get; set; }
    }

    public sealed class SyncBatchDto
    {
        public List<SyncOutcomeDto>? Items { get; set; }
    }

    public sealed class SyncWorkspaceDto
    {
        public ProfileUpsertDto? Profile { get; set; }
        public List<SyncOutcomeDto>? Items { get; set; }
        public List<StorySyncDto>? Stories { get; set; }
    }

    private static object MapStory(StoryBankItem s) => new
    {
        id = s.Id,
        title = s.Title,
        tags = s.Tags,
        situation = s.Situation,
        task = s.Task,
        action = s.Action,
        result = s.Result,
        createdAt = s.CreatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        updatedAt = s.UpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
    };
}
