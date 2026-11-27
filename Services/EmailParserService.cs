using System.Globalization;
using System.Text.RegularExpressions;
using JobHuntHQ.Models;

namespace JobHuntHQ.Services;

public sealed class ParsedEmailResult
{
    public string Company { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public ApplicationStatus DetectedStatus { get; set; } = ApplicationStatus.Applied;
    public DateOnly? DateApplied { get; set; }
    public string? MeetingUrl { get; set; }
    public DateTime? InterviewScheduledLocal { get; set; }
    public int InterviewDurationMinutes { get; set; } = 30;
    public string? TimeZoneLabel { get; set; }
    public string? Interviewer { get; set; }
    public string? ConfirmationReference { get; set; }
    public string SummaryNote { get; set; } = string.Empty;
}

/// <summary>
/// Pure deterministic, zero-cost ($0) ATS &amp; interview email parser.
/// Lets any user paste a confirmation, rejection, or Microsoft Teams / Zoom /
/// Ashby interview email directly into JobHunt HQ without needing Gmail OAuth
/// verification or any paid AI API.
/// </summary>
public static class EmailParserService
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""'\)\]]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedEmailResult Parse(string rawEmailText, string? companyOverride = null, string? roleOverride = null)
    {
        var text = (rawEmailText ?? string.Empty).Replace("\r\n", "\n").Trim();
        var lower = text.ToLowerInvariant();
        var result = new ParsedEmailResult();

        // 1. Extract optional header fields (From:, Subject:, Date:) if the user copied them.
        var subject = ExtractHeader(text, "Subject");
        var from = ExtractHeader(text, "From");

        // 2. Detect meeting / scheduling URLs (Teams, Zoom, Google Meet, Ashby, Calendly).
        var urls = UrlRegex.Matches(text).Select(m => m.Value.TrimEnd('.', ',', ';')).ToList();
        result.MeetingUrl = urls.FirstOrDefault(u =>
            u.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || u.Contains("zoom.us/j/", StringComparison.OrdinalIgnoreCase)
            || u.Contains("meet.google.com/", StringComparison.OrdinalIgnoreCase)
            || u.Contains("you.ashbyhq.com/meeting", StringComparison.OrdinalIgnoreCase)
            || u.Contains("calendly.com/", StringComparison.OrdinalIgnoreCase));

        // 3. Detect status (Interview > Rejected > Applied).
        var isInterview = result.MeetingUrl is not null
            || Regex.IsMatch(lower, @"\b(confirm your interview|your interview with|submit your availability|schedule.*interview|interview invitation|ta screen|recruiter screen)\b");
        var isRejected = !isInterview && Regex.IsMatch(lower,
            @"\b(decided to move forward with (other )?candidates|proceed with other candidates|not moving forward|will not be moving forward|unfortunately|other candidates whose qualifications|position has been filled)\b");

        if (isInterview)
        {
            result.DetectedStatus = ApplicationStatus.Interview;
        }
        else if (isRejected)
        {
            result.DetectedStatus = ApplicationStatus.Rejected;
        }
        else
        {
            result.DetectedStatus = ApplicationStatus.Applied;
            result.DateApplied = DateOnly.FromDateTime(DateTime.Today);
        }

        // 4. Extract Company & RoleTitle using common Workday, Lever, Ashby, Workable, Greenhouse, NEOGOV patterns.
        var (company, role) = ExtractCompanyAndRole(text, subject, from);
        result.Company = !string.IsNullOrWhiteSpace(companyOverride) ? companyOverride.Trim() : company;
        result.RoleTitle = !string.IsNullOrWhiteSpace(roleOverride) ? roleOverride.Trim() : role;

        // 5. Extract interview date/time, duration, timezone, and interviewer when present.
        if (result.DetectedStatus == ApplicationStatus.Interview)
        {
            ExtractInterviewDetails(text, from, result);
        }

        // 6. Build a clean confirmation reference.
        var meetingIdMatch = Regex.Match(text, @"Meeting\s+ID:\s*([0-9 \-]+)", RegexOptions.IgnoreCase);
        var passcodeMatch = Regex.Match(text, @"Passcode:\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (meetingIdMatch.Success)
        {
            var mid = meetingIdMatch.Groups[1].Value.Trim();
            result.ConfirmationReference = passcodeMatch.Success
                ? $"Meeting ID: {mid} | Passcode: {passcodeMatch.Groups[1].Value.Trim()}"
                : $"Meeting ID: {mid}";
        }
        else
        {
            var ats = DetectAtsName(lower);
            result.ConfirmationReference = ats is not null
                ? $"Pasted {ats} email ({DateTime.Today:yyyy-MM-dd})"
                : $"Pasted email ({DateTime.Today:yyyy-MM-dd})";
        }

        result.SummaryNote = $"Parsed from pasted email ({result.DetectedStatus}) on {DateTime.Today:yyyy-MM-dd}.";
        return result;
    }

    private static string? ExtractHeader(string text, string headerName)
    {
        var m = Regex.Match(text, $@"^{headerName}:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static (string Company, string Role) ExtractCompanyAndRole(string text, string? subject, string? from)
    {
        var company = string.Empty;
        var role = string.Empty;

        // Pattern A: "Your interview with <Company> – <Role>" or "for the <Role> role/position at <Company>"
        var mRoleAtCo = Regex.Match(text,
            @"(?:application\s+for\s+the|interview\s+for\s+the|applying\s+to\s+the|received\s+your\s+application\s+for)\s+([^\n\.\!]{3,110}?)\s+(?:role|position|job)\s+at\s+([A-Z][A-Za-z0-9&,\.\- ]{1,70}?)(?:[\.\!\n,]|$)",
            RegexOptions.IgnoreCase);
        if (mRoleAtCo.Success)
        {
            role = CleanField(mRoleAtCo.Groups[1].Value);
            company = CleanField(mRoleAtCo.Groups[2].Value);
        }

        // Pattern B: "Your application for the <Role> job was submitted successfully" (Workable)
        if (string.IsNullOrWhiteSpace(role))
        {
            var mWorkableRole = Regex.Match(text,
                @"application\s+for\s+(?:the\s+)?([^\n\.]{3,110}?)\s+(?:job|position|role)\b",
                RegexOptions.IgnoreCase);
            if (mWorkableRole.Success)
            {
                role = CleanField(mWorkableRole.Groups[1].Value);
            }
        }

        // Pattern C: "received your application for <Role>" (Lever)
        if (string.IsNullOrWhiteSpace(role))
        {
            var mLeverRole = Regex.Match(text,
                @"received\s+your\s+application\s+for\s+([^\n\.,]{3,110}?)(?:,|\.|and\s+we|\n|$)",
                RegexOptions.IgnoreCase);
            if (mLeverRole.Success)
            {
                role = CleanField(mLeverRole.Groups[1].Value);
            }
        }

        // Pattern D: Subject line patterns ("Thanks for applying to <Company>", "Your interview with <Company> – <Role>", "<Role> - <Company>")
        if (!string.IsNullOrWhiteSpace(subject))
        {
            var mInterviewSub = Regex.Match(subject, @"interview\s+with\s+([A-Za-z0-9&\.\- ]+?)\s*[–—\-:]\s*(.+)$", RegexOptions.IgnoreCase);
            if (mInterviewSub.Success)
            {
                if (string.IsNullOrWhiteSpace(company)) company = CleanField(mInterviewSub.Groups[1].Value);
                if (string.IsNullOrWhiteSpace(role)) role = CleanField(mInterviewSub.Groups[2].Value);
            }

            if (string.IsNullOrWhiteSpace(company))
            {
                var mThanks = Regex.Match(subject, @"(?:applying\s+to|application\s+to|received\s+by)\s+([A-Za-z0-9&\.\-, ]{2,70}?)(?:[\!\.\n]|$)", RegexOptions.IgnoreCase);
                if (mThanks.Success) company = CleanField(mThanks.Groups[1].Value);
            }

            if (string.IsNullOrWhiteSpace(company))
            {
                var mColon = Regex.Match(subject, @"^([A-Za-z0-9&\.\- ]{2,50}):\s+.*?(?:update|application|status)", RegexOptions.IgnoreCase);
                if (mColon.Success) company = CleanField(mColon.Groups[1].Value);
            }
        }

        // Pattern E: Body "Thank you for your interest in <Company>" / "applying to <Company>"
        if (string.IsNullOrWhiteSpace(company))
        {
            var mInterest = Regex.Match(text,
                @"(?:interest\s+in|applying\s+to|application\s+to|received\s+by)\s+([A-Z][A-Za-z0-9&\.\-, ]{1,65}?)(?:[\!\.\n]|$)");
            if (mInterest.Success) company = CleanField(mInterest.Groups[1].Value);
        }

        // Pattern F: From display name fallback (ignoring generic ATS senders like Workable / noreply)
        if (string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(from))
        {
            var display = Regex.Replace(from, @"<[^>]+>", "").Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(display)
                && !display.Contains("workable", StringComparison.OrdinalIgnoreCase)
                && !display.Contains("lever", StringComparison.OrdinalIgnoreCase)
                && !display.Contains("greenhouse", StringComparison.OrdinalIgnoreCase)
                && !display.Contains("noreply", StringComparison.OrdinalIgnoreCase)
                && !display.Contains("no-reply", StringComparison.OrdinalIgnoreCase))
            {
                company = Regex.Replace(display, @"\s+(Hiring Team|Talent Team|Recruiting|Careers)$", "", RegexOptions.IgnoreCase).Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            role = "Software Engineer";
        }

        return (company, role);
    }

    private static void ExtractInterviewDetails(string text, string? from, ParsedEmailResult result)
    {
        // Duration (e.g. "30m", "45 minutes", "60 min")
        var mDur = Regex.Match(text, @"\b(15|20|30|45|60|90)\s*(?:m\b|min(?:ute)?s?\b)", RegexOptions.IgnoreCase);
        if (mDur.Success && int.TryParse(mDur.Groups[1].Value, out var mins))
        {
            result.InterviewDurationMinutes = mins;
        }

        // Timezone label (EST, EDT, CST, PST, ET, PT, UTC)
        var mTz = Regex.Match(text, @"\b(EST|EDT|CST|CDT|MST|MDT|PST|PDT|UTC|GMT)\b");
        if (mTz.Success)
        {
            result.TimeZoneLabel = mTz.Groups[1].Value;
        }

        // Date & Time (e.g. "Fri, Nov 27, 2026 at 9:00 AM" or "November 27, 2026 9:00 AM")
        var mDate = Regex.Match(text,
            @"(?:(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)[a-z]*,?\s+)?((?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2},?\s+\d{4})\s*(?:at\s+|@\s*|,?\s*)(\d{1,2}:\d{2}\s*(?:AM|PM))",
            RegexOptions.IgnoreCase);
        if (mDate.Success)
        {
            var combined = $"{mDate.Groups[1].Value.Replace(".", "")} {mDate.Groups[2].Value}";
            string[] formats =
            {
                "MMM d, yyyy h:mm tt", "MMM d yyyy h:mm tt",
                "MMMM d, yyyy h:mm tt", "MMMM d yyyy h:mm tt",
                "MMM dd, yyyy h:mm tt", "MMMM dd, yyyy h:mm tt"
            };
            if (DateTime.TryParseExact(combined, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                result.InterviewScheduledLocal = parsed;
            }
        }

        // Interviewer ("You'll be meeting: Name, Title")
        var mMeeting = Regex.Match(text, @"(?:You'll be meeting|Interviewer|Host):\s*([^\n]{3,120})", RegexOptions.IgnoreCase);
        if (mMeeting.Success)
        {
            result.Interviewer = mMeeting.Groups[1].Value.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(from))
        {
            result.Interviewer = from.Trim();
        }
    }

    private static string? DetectAtsName(string lower)
    {
        if (lower.Contains("workable")) return "Workable";
        if (lower.Contains("myworkday") || lower.Contains("workday")) return "Workday";
        if (lower.Contains("hire.lever.co") || lower.Contains("lever")) return "Lever";
        if (lower.Contains("ashbyhq")) return "Ashby";
        if (lower.Contains("greenhouse")) return "Greenhouse";
        if (lower.Contains("governmentjobs.com") || lower.Contains("neogov")) return "NEOGOV";
        if (lower.Contains("pinpoint")) return "Pinpoint";
        if (lower.Contains("teams.microsoft.com")) return "Microsoft Teams";
        return null;
    }

    private static string CleanField(string value) =>
        Regex.Replace(value.Trim().Trim('-', '–', '—', ':', ',', '.', '!'), @"\s+", " ");
}
