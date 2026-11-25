using System.Text;
using System.Text.Json;
using JobHuntHQ.Models;

namespace JobHuntHQ.Services;

public sealed class InterviewPacketData
{
    public required DateTime GeneratedAtUtc { get; init; }
    public required JobApplication Application { get; init; }
    public required SearchProfile? Profile { get; init; }
    public required ResumeVersion? Resume { get; init; }
    public required ResumeTextResult ResumeText { get; init; }
    public required IReadOnlyList<InterviewRound> Rounds { get; init; }
    public required InterviewRound? FocusRound { get; init; }
    public required IReadOnlyList<StoryBankItem> Stories { get; init; }
    public required HashSet<int> MatchedStoryIds { get; init; }
    public required IReadOnlyList<(string Title, string Prompt)> ReadyPrompts { get; init; }

    public string? SourceUrl => Application.PostingUrl ?? Application.Url;
}

/// <summary>
/// Builds the bring-your-own-AI Interview Prep Packet (M5, packetVersion 1)
/// in Markdown and JSON, plus RFC 5545 .ics calendar invites for scheduled
/// interview rounds.
/// </summary>
public class InterviewPacketService
{
    public const int PacketVersion = 1;

    private readonly ResumeTextService _resumeText;

    public InterviewPacketService(ResumeTextService resumeText)
    {
        _resumeText = resumeText;
    }

    private static readonly string[] CoachingRules =
    {
        "You are coaching the candidate for a real live interview — never invent employers, titles, dates, projects, metrics, or stories.",
        "Anchor every suggested talking point, STAR story, or technical example strictly to the resume text, STAR Story Bank, and profile facts in this packet.",
        "When the posting asks for a tool, domain, or skill that is NOT in the candidate's resume or STAR stories, explicitly call out the gap and help frame an honest, confident transferable-experience answer — never pretend the candidate has used something they haven't.",
        "Tailor depth, pacing, and tone to the active interview round type (for example: concise alignment & logistics for a Recruiter Screen; architectural trade-offs & concrete ownership for Technical / Hiring Manager rounds).",
        "If compensation or logistics come up, reference only the candidate's stated pay floor, remote preference, and standing answers — flag anything unspecified so the candidate decides beforehand."
    };

    public async Task<InterviewPacketData> BuildPacketAsync(
        JobApplication application,
        SearchProfile? profile,
        ResumeVersion? resume,
        IReadOnlyList<InterviewRound> rounds,
        InterviewRound? focusRound,
        IReadOnlyList<StoryBankItem> stories,
        CancellationToken cancellationToken = default)
    {
        var resumeText = resume is null
            ? new ResumeTextResult(null, false, null)
            : await _resumeText.ExtractAsync(resume, cancellationToken);

        var matchedIds = MatchStoryIds(application, profile, stories);

        return new InterviewPacketData
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Application = application,
            Profile = profile,
            Resume = resume,
            ResumeText = resumeText,
            Rounds = rounds,
            FocusRound = focusRound,
            Stories = stories,
            MatchedStoryIds = matchedIds,
            ReadyPrompts = BuildReadyPrompts(application, focusRound)
        };
    }

    public static string BuildShortPrompt(JobApplication application, InterviewRound? focusRound)
    {
        var roundDesc = focusRound is not null
            ? $"my upcoming {focusRound.RoundType} (Round {focusRound.RoundNumber})"
            : "my upcoming interview";
        return $"Help me prepare for {roundDesc} for the {application.RoleTitle} role at {application.Company} " +
               "using the JobHunt HQ interview prep packet I'll attach. Rules: use only real facts from my resume " +
               "and STAR stories in the packet, flag any skill gaps honestly instead of inventing experience, and " +
               "coach me interactively.";
    }

    /// <summary>Finds which STAR stories match keywords in the posting or the user's must-haves.</summary>
    public static HashSet<int> MatchStoryIds(
        JobApplication application,
        SearchProfile? profile,
        IReadOnlyList<StoryBankItem> stories)
    {
        var matched = new HashSet<int>();
        if (stories.Count == 0)
        {
            return matched;
        }

        var haystack = $"{application.RoleTitle}\n{application.PostingText}";
        var mustHaves = FitCheckService.ParseList(profile?.MustHaves);

        foreach (var story in stories)
        {
            var tags = FitCheckService.ParseList(story.Tags);
            var tagHit = tags.Any(tag =>
                tag.Length >= 2 && haystack.Contains(tag, StringComparison.OrdinalIgnoreCase));
            var mustHaveHit = mustHaves.Any(mh =>
                (story.Tags?.Contains(mh, StringComparison.OrdinalIgnoreCase) ?? false)
                || story.Title.Contains(mh, StringComparison.OrdinalIgnoreCase)
                || (story.Action?.Contains(mh, StringComparison.OrdinalIgnoreCase) ?? false));

            if (tagHit || mustHaveHit)
            {
                matched.Add(story.Id);
            }
        }

        return matched;
    }

    public string BuildMarkdown(InterviewPacketData packet)
    {
        var app = packet.Application;
        var profile = packet.Profile;
        var focus = packet.FocusRound;
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine("packetType: interview-prep");
        sb.AppendLine($"packetVersion: {PacketVersion}");
        sb.AppendLine($"generatedAt: {packet.GeneratedAtUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"jobhuntApplicationId: {app.Id}");
        sb.AppendLine($"activeRound: {YamlValue(focus is null ? "General Interview Prep" : $"Round {focus.RoundNumber} - {focus.RoundType}")}");
        sb.AppendLine($"sourceUrl: {YamlValue(packet.SourceUrl)}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# Interview prep packet — {app.Company} — {app.RoleTitle}");
        sb.AppendLine();
        sb.AppendLine("Generated by JobHunt HQ for interview preparation with your own AI assistant.");
        sb.AppendLine();

        sb.AppendLine("## 1. Coaching rules for the AI assistant");
        sb.AppendLine();
        foreach (var rule in CoachingRules)
        {
            sb.AppendLine($"- {rule}");
        }
        sb.AppendLine();

        sb.AppendLine("## 2. Active interview round & schedule");
        sb.AppendLine();
        if (focus is null)
        {
            sb.AppendLine("(No specific round scheduled yet — prepare for an initial Recruiter / Hiring Manager interview.)");
        }
        else
        {
            sb.AppendLine($"- Active round: Round {focus.RoundNumber} — {focus.RoundType}");
            if (focus.ScheduledLocal.HasValue)
            {
                var tz = string.IsNullOrWhiteSpace(focus.TimeZoneLabel) ? "" : $" {focus.TimeZoneLabel}";
                sb.AppendLine($"- Scheduled: {focus.ScheduledLocal.Value:yyyy-MM-dd HH:mm}{tz} ({focus.DurationMinutes} min)");
            }
            if (!string.IsNullOrWhiteSpace(focus.Interviewers))
            {
                sb.AppendLine($"- Interviewer(s): {focus.Interviewers}");
            }
            if (!string.IsNullOrWhiteSpace(focus.PrepNotes))
            {
                sb.AppendLine($"- Round focus / prep notes: {focus.PrepNotes.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(focus.QuestionsToAsk))
            {
                sb.AppendLine($"- Candidate's planned questions to ask: {focus.QuestionsToAsk.Trim()}");
            }
        }

        if (packet.Rounds.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("### All tracked rounds on this application");
            sb.AppendLine();
            foreach (var r in packet.Rounds)
            {
                var state = r.Completed ? "Completed" : "Upcoming";
                var when = r.ScheduledLocal.HasValue ? r.ScheduledLocal.Value.ToString("yyyy-MM-dd HH:mm") : "unscheduled";
                sb.AppendLine($"- Round {r.RoundNumber} ({r.RoundType}) — {state} ({when})");
                if (!string.IsNullOrWhiteSpace(r.DebriefNotes))
                {
                    sb.AppendLine($"  - Notes / debrief: {r.DebriefNotes.Trim()}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("## 3. Role, compensation anchor & company research");
        sb.AppendLine();
        sb.AppendLine($"- Company: {app.Company}");
        sb.AppendLine($"- Role: {app.RoleTitle}");
        if (!string.IsNullOrWhiteSpace(app.Location))
        {
            sb.AppendLine($"- Location: {app.Location}");
        }
        if (app.PayMin.HasValue || app.PayMax.HasValue)
        {
            sb.AppendLine($"- Pay listed in posting: {Money(app.PayMin)} – {Money(app.PayMax)}");
        }
        else
        {
            sb.AppendLine("- Pay listed in posting: not posted");
        }
        if (profile?.PayFloor is int floor)
        {
            sb.AppendLine($"- Candidate's pay floor: {Money(floor)} / year");
        }
        if (!string.IsNullOrWhiteSpace(profile?.RemotePreference))
        {
            sb.AppendLine($"- Candidate's remote preference: {profile.RemotePreference}");
        }
        if (!string.IsNullOrWhiteSpace(app.ElevatorPitch))
        {
            sb.AppendLine();
            sb.AppendLine("### Candidate's 60-second pitch draft");
            sb.AppendLine();
            sb.AppendLine(app.ElevatorPitch.Trim());
        }
        if (!string.IsNullOrWhiteSpace(app.CompanyResearch))
        {
            sb.AppendLine();
            sb.AppendLine("### Candidate's company & team research");
            sb.AppendLine();
            sb.AppendLine(app.CompanyResearch.Trim());
        }
        sb.AppendLine();
        sb.AppendLine("### Posting text (verbatim)");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(app.PostingText)
            ? "(No posting text captured yet — ask the candidate to paste or fetch the job description so questions match exact requirements.)"
            : app.PostingText.Trim());
        sb.AppendLine();

        sb.AppendLine("## 4. Standing answers & logistics");
        sb.AppendLine();
        sb.AppendLine(profile is null || string.IsNullOrWhiteSpace(profile.StandingAnswers)
            ? "(No standing answers saved.)"
            : profile.StandingAnswers.Trim());
        sb.AppendLine();

        sb.AppendLine("## 5. Resume sent to this employer");
        sb.AppendLine();
        if (packet.Resume is null)
        {
            sb.AppendLine("(No resume version linked yet.)");
        }
        else
        {
            sb.AppendLine($"- Resume version: {packet.Resume.Name}");
            if (!string.IsNullOrWhiteSpace(packet.Resume.FileName))
            {
                sb.AppendLine($"- File: {packet.Resume.FileName}");
            }
            if (packet.ResumeText.Text is not null)
            {
                sb.AppendLine();
                sb.AppendLine("### Extracted resume text");
                sb.AppendLine();
                sb.AppendLine(packet.ResumeText.Text);
            }
            else if (packet.ResumeText.Note is not null)
            {
                sb.AppendLine();
                sb.AppendLine(packet.ResumeText.Note);
            }
        }
        sb.AppendLine();

        sb.AppendLine("## 6. Candidate's STAR Story Bank (real experiences only)");
        sb.AppendLine();
        if (packet.Stories.Count == 0)
        {
            sb.AppendLine("(No STAR stories saved in the Story Bank yet — draw examples strictly from the resume text in section 5, and help the candidate turn 2–3 resume bullets into structured STAR stories.)");
        }
        else
        {
            var orderedStories = packet.Stories
                .OrderByDescending(s => packet.MatchedStoryIds.Contains(s.Id))
                .ThenBy(s => s.Title);
            foreach (var story in orderedStories)
            {
                var badge = packet.MatchedStoryIds.Contains(story.Id) ? " [MATCHES POSTING KEYWORDS]" : "";
                sb.AppendLine($"### {story.Title}{badge}");
                if (!string.IsNullOrWhiteSpace(story.Tags))
                {
                    sb.AppendLine($"- Tags: {story.Tags}");
                }
                if (!string.IsNullOrWhiteSpace(story.Situation))
                {
                    sb.AppendLine($"- Situation: {story.Situation.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(story.Task))
                {
                    sb.AppendLine($"- Task: {story.Task.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(story.Action))
                {
                    sb.AppendLine($"- Action: {story.Action.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(story.Result))
                {
                    sb.AppendLine($"- Result: {story.Result.Trim()}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("## 7. Ready interview prompts");
        sb.AppendLine();
        foreach (var (title, prompt) in packet.ReadyPrompts)
        {
            sb.AppendLine($"### {title}");
            sb.AppendLine();
            sb.AppendLine(prompt);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string BuildJson(InterviewPacketData packet)
    {
        var app = packet.Application;
        var profile = packet.Profile;
        var payload = new
        {
            packetType = "interview-prep",
            packetVersion = PacketVersion,
            generatedAt = packet.GeneratedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            jobhuntApplicationId = app.Id,
            sourceUrl = packet.SourceUrl,
            coachingRules = CoachingRules,
            focusRound = packet.FocusRound is null ? null : new
            {
                roundNumber = packet.FocusRound.RoundNumber,
                roundType = packet.FocusRound.RoundType,
                scheduledLocal = packet.FocusRound.ScheduledLocal?.ToString("yyyy-MM-ddTHH:mm"),
                timeZoneLabel = packet.FocusRound.TimeZoneLabel,
                durationMinutes = packet.FocusRound.DurationMinutes,
                interviewers = packet.FocusRound.Interviewers,
                prepNotes = packet.FocusRound.PrepNotes,
                questionsToAsk = packet.FocusRound.QuestionsToAsk
            },
            allRounds = packet.Rounds.Select(r => new
            {
                roundNumber = r.RoundNumber,
                roundType = r.RoundType,
                scheduledLocal = r.ScheduledLocal?.ToString("yyyy-MM-ddTHH:mm"),
                completed = r.Completed,
                debriefNotes = r.DebriefNotes
            }),
            roleAndCompany = new
            {
                company = app.Company,
                roleTitle = app.RoleTitle,
                location = app.Location,
                payMin = app.PayMin,
                payMax = app.PayMax,
                candidatePayFloor = profile?.PayFloor,
                remotePreference = profile?.RemotePreference,
                elevatorPitch = app.ElevatorPitch,
                companyResearch = app.CompanyResearch,
                postingText = app.PostingText
            },
            standingAnswers = profile?.StandingAnswers,
            resume = packet.Resume is null ? null : new
            {
                name = packet.Resume.Name,
                fileName = packet.Resume.FileName,
                textIncluded = packet.ResumeText.Text is not null,
                truncated = packet.ResumeText.Truncated,
                text = packet.ResumeText.Text,
                note = packet.ResumeText.Note
            },
            storyBank = packet.Stories.Select(s => new
            {
                title = s.Title,
                tags = s.Tags,
                matchesPosting = packet.MatchedStoryIds.Contains(s.Id),
                situation = s.Situation,
                task = s.Task,
                action = s.Action,
                result = s.Result
            }),
            readyPrompts = packet.ReadyPrompts.Select(p => new { title = p.Title, prompt = p.Prompt })
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public static string BuildIcsCalendarInvite(JobApplication app, InterviewRound round, string? cheatSheetUrl)
    {
        var startLocal = round.ScheduledLocal ?? DateTime.Now.AddDays(1);
        var endLocal = startLocal.AddMinutes(round.DurationMinutes > 0 ? round.DurationMinutes : 45);
        var uid = $"jobhunthq-app-{app.Id}-round-{round.Id}@jobhunthq.local";
        var summary = $"Interview: {app.Company} — {app.RoleTitle} (Round {round.RoundNumber}: {round.RoundType})";

        var descLines = new List<string>
        {
            $"Role: {app.RoleTitle} at {app.Company}",
            $"Round {round.RoundNumber}: {round.RoundType} ({round.DurationMinutes} min)"
        };
        if (!string.IsNullOrWhiteSpace(round.Interviewers))
        {
            descLines.Add($"Interviewer(s): {round.Interviewers}");
        }
        if (!string.IsNullOrWhiteSpace(round.MeetingUrl))
        {
            descLines.Add($"Meeting link: {round.MeetingUrl}");
        }
        if (!string.IsNullOrWhiteSpace(cheatSheetUrl))
        {
            descLines.Add($"JobHunt HQ Live Cheat Sheet: {cheatSheetUrl}");
        }
        if (!string.IsNullOrWhiteSpace(round.PrepNotes))
        {
            descLines.Add($"Prep focus: {round.PrepNotes}");
        }

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//JobHunt HQ//Interview Workspace//EN\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:{IcsEscape(uid)}\r\n");
        sb.Append($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}\r\n");
        sb.Append($"DTSTART:{startLocal:yyyyMMddTHHmmss}\r\n");
        sb.Append($"DTEND:{endLocal:yyyyMMddTHHmmss}\r\n");
        sb.Append($"SUMMARY:{IcsEscape(summary)}\r\n");
        if (!string.IsNullOrWhiteSpace(round.MeetingUrl))
        {
            sb.Append($"LOCATION:{IcsEscape(round.MeetingUrl)}\r\n");
        }
        sb.Append($"DESCRIPTION:{IcsEscape(string.Join("\n", descLines))}\r\n");
        sb.Append("END:VEVENT\r\n");
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static IReadOnlyList<(string Title, string Prompt)> BuildReadyPrompts(
        JobApplication app,
        InterviewRound? focusRound)
    {
        var roundLabel = focusRound is not null
            ? $"{focusRound.RoundType} (Round {focusRound.RoundNumber})"
            : "initial interview";

        return new (string, string)[]
        {
            ("Realistic mock interview",
             $"Run a realistic {roundLabel} for the {app.RoleTitle} role at {app.Company} using this packet. " +
             "Ask me ONE question at a time. Wait for my response, then critique my answer for clarity, " +
             "conciseness, and alignment with the posting — suggesting how I could weave in specific real facts " +
             "from my resume or STAR Story Bank without inventing anything."),
            ("Sharpen my 60-second pitch",
             $"Using only my resume, target roles, and the {app.Company} posting in this packet, draft a crisp, " +
             $"natural 60-second answer to \"Tell me about yourself and why {app.Company}?\" tailored for a " +
             $"{roundLabel}. Give me a bulleted spoken outline so I sound conversational, not scripted."),
            ("Requirement-to-story matrix & gap bridges",
             "Map every key technical and behavioral requirement in this posting to concrete evidence from my " +
             "resume and STAR Story Bank. For any requirement not directly shown in my background, flag the gap " +
             "clearly and help me craft an honest, confident transferable-experience answer."),
            ("High-signal questions to ask them",
             $"Suggest 6 sharp, genuine questions I can ask during this {roundLabel} at {app.Company} to evaluate " +
             "engineering culture, day-to-day expectations, success in the first 90 days, and next steps — " +
             "avoiding generic filler questions."),
            ("Post-interview thank-you note",
             $"Using my debrief notes and the {roundLabel} context in this packet, draft a warm, concise " +
             "thank-you email under 140 words that references what we actually discussed and reinforces my fit " +
             "using only real facts.")
        };
    }

    private static string IcsEscape(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");

    private static string Money(int? value) =>
        value.HasValue ? "$" + value.Value.ToString("N0") : "—";

    private static string YamlValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
