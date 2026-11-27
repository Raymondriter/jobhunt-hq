using System.Text;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Controllers;

/// <summary>
/// Read-only, subscribable iCalendar feed (Google Calendar "From URL", Apple
/// Calendar, Outlook). The URL carries a personal token limited to the
/// <c>calendar:read</c> scope, so it can only ever expose interview times and
/// follow-up dates — and it can be revoked from the Profile page at any time.
/// </summary>
[AllowAnonymous]
public class CalendarController : Controller
{
    private readonly ApplicationDbContext _context;

    public CalendarController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /calendar/feed/{key}.ics
    [HttpGet("/calendar/feed/{key}.ics")]
    [HttpGet("/calendar/feed/{key}")]
    public async Task<IActionResult> Feed(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(ApiToken.Prefix, StringComparison.Ordinal) || key.Length < 20)
        {
            return NotFound();
        }

        var hash = ApiToken.ComputeSha256Hex(key);
        var token = await _context.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null || token.IsRevoked || !token.HasScope(ApiToken.CalendarScope))
        {
            return NotFound();
        }

        token.LastUsedUtc = DateTime.UtcNow;
        var apps = await _context.JobApplications
            .Include(a => a.InterviewRounds)
            .Where(a => a.UserId == token.UserId)
            .AsSplitQuery()
            .ToListAsync();
        await _context.SaveChangesAsync();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var ics = InterviewPacketService.BuildCalendarFeed(apps, baseUrl);
        Response.Headers.CacheControl = "private, max-age=900";
        return File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8");
    }
}
