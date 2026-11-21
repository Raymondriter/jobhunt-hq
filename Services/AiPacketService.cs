using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHuntHQ.Models;

namespace JobHuntHQ.Services;

/// <summary>
/// The structured content of one AI packet (M4 phase 2, packetVersion 2),
/// built once and rendered to both Markdown and JSON so the two downloads
/// can never drift apart.
/// </summary>
public sealed class AiPacketData
{
    public required DateTime GeneratedAtUtc { get; init; }
    public required JobApplication Application { get; init; }
    public required SearchProfile? Profile { get; init; }
    public required ResumeVersion? Resume { get; init; }
    public required string AtsKey { get; init; }
    public required string AtsName { get; init; }
    public required string AtsFormMap { get; init; }
    public required ResumeTextResult ResumeText { get; init; }
    public required IReadOnlyList<string> Cautions { get; init; }
    public required IReadOnlyList<(string Title, string Prompt)> ReadyPrompts { get; init; }

    public string? SourceUrl => Application.PostingUrl ?? Application.Url;
    public IReadOnlyList<string> FitReasonLines =>
        string.IsNullOrWhiteSpace(Application.FitReasons)
            ? Array.Empty<string>()
            : Application.FitReasons.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Builds the bring-your-own-AI packet (M4, v2 in phase 2): a self-contained
/// document a user hands to their own AI assistant. Structure follows the
/// research pack's packet spec: YAML front-matter, then instructions with
/// hard rules (use only the facts in the packet, never invent, NEVER submit
/// — stop and ask the user), the posting, the fit verdict with evidence, the
/// user's profile and standing answers, the chosen resume's full text when
/// extractable, a form map for the detected ATS, cautions, ready prompts,
/// and the return format. JobHunt HQ itself never submits anything — the
/// user reviews and submits everything themselves, outside this app.
/// EEO / voluntary self-ID data is never added: standing answers are the
/// user's own authored text, and nothing demographic is inferred.
/// </summary>
public class AiPacketService
{
    public const int PacketVersion = 2;

    private readonly ResumeTextService _resumeText;

    public AiPacketService(ResumeTextService resumeText)
    {
        _resumeText = resumeText;
    }

    private static readonly string[] Instructions =
    {
        "You are helping the candidate prepare this application — not submit it.",
        "Use ONLY the facts in this packet. Never invent experience, credentials, employers, dates, metrics, or answers.",
        "Never click the final Submit button (or its equivalent on any form). Stop before submission and ask the user to review and submit themselves.",
        "Where the packet doesn't say something, say so and ask the user — do not guess or fill gaps with plausible-sounding details.",
        "Knockout questions — work authorization, sponsorship (now or in the future), location/relocation, required licences or certifications: answer ONLY from the standing answers. If they don't cover it, stop and ask.",
        "Use the standing answers verbatim where they fit a question; flag any question they don't cover instead of improvising.",
        "If the posting says AI assistance is prohibited or restricted, flag that to the user immediately and follow the posting.",
        "Respect the fit check: do not override a deal-breaker or Blocked verdict — if you disagree, say why and let the user decide.",
        "Draft in the candidate's voice, from provided facts only."
    };

    private static readonly string[] ReturnFormat =
    {
        "A summary of everything you filled in or drafted, field by field, so the user can review it before submitting.",
        "Anything you flagged: knockout questions not covered by standing answers, missing facts, contradictions between the resume and the posting, or an employer AI-use restriction.",
        "A suggested application status and next action with a follow-up date, in a form the user can paste back into JobHunt HQ's preparation view.",
        "The user reviews and submits the application themselves — always."
    };

    /// <summary>Assembles the packet data (extracting resume text when possible).</summary>
    /// <param name="otherApplicationsAtCompany">How many OTHER applications the user already tracks at the same company — drives the duplicate-application caution.</param>
    public async Task<AiPacketData> BuildPacketAsync(
        JobApplication application,
        SearchProfile? profile,
        ResumeVersion? resume,
        int otherApplicationsAtCompany,
        CancellationToken cancellationToken = default)
    {
        var atsKey = AtsInfo.DetectKey(application.PostingUrl ?? application.Url);
        var resumeText = resume is null
            ? new ResumeTextResult(null, false, null)
            : await _resumeText.ExtractAsync(resume, cancellationToken);

        return new AiPacketData
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Application = application,
            Profile = profile,
            Resume = resume,
            AtsKey = atsKey,
            AtsName = AtsInfo.DisplayName(atsKey),
            AtsFormMap = AtsInfo.FormMap(atsKey),
            ResumeText = resumeText,
            Cautions = BuildCautions(application, resume, resumeText, otherApplicationsAtCompany),
            ReadyPrompts = BuildReadyPrompts(application)
        };
    }

    /// <summary>The short, self-contained prompt used by the ChatGPT/Claude deep links and the Copy prompt button. The packet itself is never put in a URL.</summary>
    public static string BuildShortPrompt(JobApplication application) =>
        $"Help me apply for the {application.RoleTitle} role at {application.Company} using the JobHunt HQ " +
        "application packet I'll attach. Rules: use only facts in the packet, never invent anything, and " +
        "never submit — stop and ask me before any final submission.";

    // ---------- Markdown rendering ----------

    public string BuildMarkdown(AiPacketData packet)
    {
        var application = packet.Application;
        var sb = new StringBuilder();

        // YAML front-matter (machine header) — must be the very first thing in the file.
        sb.AppendLine("---");
        sb.AppendLine($"packetVersion: {PacketVersion}");
        sb.AppendLine($"generatedAt: {packet.GeneratedAtUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"jobhuntApplicationId: {application.Id}");
        sb.AppendLine($"atsDetected: {packet.AtsKey}");
        sb.AppendLine($"sourceUrl: {YamlValue(packet.SourceUrl)}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# Job application packet — {application.Company} — {application.RoleTitle}");
        sb.AppendLine();
        sb.AppendLine("Generated by JobHunt HQ for use with your own AI assistant.");
        sb.AppendLine();

        sb.AppendLine("## 1. Instructions for the AI assistant");
        sb.AppendLine();
        foreach (var rule in Instructions)
        {
            sb.AppendLine($"- {rule}");
        }
        sb.AppendLine();

        sb.AppendLine("## 2. The posting");
        sb.AppendLine();
        sb.AppendLine($"- Company: {application.Company}");
        sb.AppendLine($"- Role: {application.RoleTitle}");
        if (!string.IsNullOrWhiteSpace(packet.SourceUrl))
        {
            sb.AppendLine($"- Posting URL: {packet.SourceUrl}");
        }
        if (!string.IsNullOrWhiteSpace(application.Location))
        {
            sb.AppendLine($"- Location: {application.Location}");
        }
        if (application.PayMin.HasValue || application.PayMax.HasValue)
        {
            sb.AppendLine($"- Pay listed in posting: {Money(application.PayMin)} – {Money(application.PayMax)}");
        }
        else
        {
            sb.AppendLine("- Pay listed in posting: not posted");
        }
        sb.AppendLine();
        sb.AppendLine("### Posting text (verbatim)");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(application.PostingText)
            ? "(No posting text was captured — ask the user for the full posting before drafting anything.)"
            : application.PostingText.Trim());
        sb.AppendLine();

        sb.AppendLine("## 3. Fit verdict");
        sb.AppendLine();
        sb.AppendLine($"Verdict: {application.FitVerdict ?? FitCheckService.VerdictNotChecked}");
        foreach (var line in packet.FitReasonLines)
        {
            sb.AppendLine($"- {line}");
        }
        sb.AppendLine();

        sb.AppendLine("## 4. Your profile snapshot");
        sb.AppendLine();
        var profile = packet.Profile;
        if (profile is null)
        {
            sb.AppendLine("(No search profile saved yet.)");
        }
        else
        {
            AppendField(sb, "Target roles", profile.TargetRoles);
            AppendField(sb, "Remote preference", profile.RemotePreference);
            if (profile.PayFloor.HasValue)
            {
                sb.AppendLine($"- Pay floor: {Money(profile.PayFloor)}");
            }
            AppendField(sb, "Must-haves", profile.MustHaves);
            AppendField(sb, "Deal-breakers", profile.DealBreakers);
            AppendField(sb, "Skip companies", profile.SkipCompanies);
        }
        sb.AppendLine();
        sb.AppendLine("### Application status in JobHunt HQ");
        sb.AppendLine();
        sb.AppendLine($"- Status: {application.Status}");
        sb.AppendLine($"- Date saved: {application.DateSaved:yyyy-MM-dd}");
        if (application.DateApplied.HasValue)
        {
            sb.AppendLine($"- Date applied: {application.DateApplied.Value:yyyy-MM-dd}");
        }
        if (!string.IsNullOrWhiteSpace(application.NextAction))
        {
            sb.AppendLine(application.NextActionDue.HasValue
                ? $"- Next action: {application.NextAction} (due {application.NextActionDue.Value:yyyy-MM-dd})"
                : $"- Next action: {application.NextAction}");
        }
        if (!string.IsNullOrWhiteSpace(application.Notes))
        {
            sb.AppendLine($"- Notes: {application.Notes.Trim()}");
        }
        sb.AppendLine();

        sb.AppendLine("## 5. Standing answers");
        sb.AppendLine();
        sb.AppendLine(profile is null || string.IsNullOrWhiteSpace(profile.StandingAnswers)
            ? "(None saved.)"
            : profile.StandingAnswers.Trim());
        sb.AppendLine();

        sb.AppendLine("## 6. Resume");
        sb.AppendLine();
        AppendResume(sb, packet);
        sb.AppendLine();

        sb.AppendLine($"## 7. ATS form map — {packet.AtsName}");
        sb.AppendLine();
        sb.AppendLine(packet.AtsFormMap);
        sb.AppendLine();

        sb.AppendLine("## 8. Cautions");
        sb.AppendLine();
        foreach (var caution in packet.Cautions)
        {
            sb.AppendLine($"- {caution}");
        }
        sb.AppendLine();

        sb.AppendLine("## 9. Ready prompts");
        sb.AppendLine();
        foreach (var (title, prompt) in packet.ReadyPrompts)
        {
            sb.AppendLine($"### {title}");
            sb.AppendLine();
            sb.AppendLine(prompt);
            sb.AppendLine();
        }

        sb.AppendLine("## 10. Return format");
        sb.AppendLine();
        sb.AppendLine("When you're done, hand back:");
        for (var i = 0; i < ReturnFormat.Length; i++)
        {
            sb.AppendLine($"{i + 1}. {ReturnFormat[i]}");
        }

        return sb.ToString();
    }

    private static void AppendResume(StringBuilder sb, AiPacketData packet)
    {
        var resume = packet.Resume;
        if (resume is null)
        {
            sb.AppendLine("(No resume version saved — ask the user which resume to use.)");
            return;
        }

        sb.AppendLine($"- Version: {resume.Name}");
        sb.AppendLine(resume.FileName is not null
            ? $"- File uploaded: {resume.FileName}"
            : "- File uploaded: none (metadata only)");
        if (!string.IsNullOrWhiteSpace(resume.Notes))
        {
            sb.AppendLine($"- Notes: {resume.Notes}");
        }

        var result = packet.ResumeText;
        if (result.Text is not null)
        {
            sb.AppendLine();
            sb.AppendLine("### Resume text (extracted from the uploaded PDF)");
            sb.AppendLine();
            sb.AppendLine(result.Text);
            if (result.Truncated)
            {
                sb.AppendLine();
                sb.AppendLine($"(Resume text truncated at {ResumeTextService.MaxChars:N0} characters — the full PDF is the complete version.)");
            }
            sb.AppendLine();
            sb.AppendLine("Note: the PDF file itself, not this text, is what gets uploaded to the application form.");
        }
        else if (result.Note is not null)
        {
            sb.AppendLine();
            sb.AppendLine(result.Note);
        }
    }

    // ---------- JSON rendering ----------

    public string BuildJson(AiPacketData packet)
    {
        var application = packet.Application;
        var profile = packet.Profile;
        var resume = packet.Resume;

        var dto = new PacketJson
        {
            PacketVersion = PacketVersion,
            GeneratedAt = packet.GeneratedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            JobhuntApplicationId = application.Id,
            AtsDetected = packet.AtsKey,
            SourceUrl = packet.SourceUrl,
            Instructions = Instructions,
            Posting = new PostingJson
            {
                Company = application.Company,
                Title = application.RoleTitle,
                Url = packet.SourceUrl,
                Location = application.Location,
                PayMin = application.PayMin,
                PayMax = application.PayMax,
                Text = string.IsNullOrWhiteSpace(application.PostingText) ? null : application.PostingText.Trim()
            },
            FitCheck = new FitCheckJson
            {
                Verdict = application.FitVerdict ?? FitCheckService.VerdictNotChecked,
                Reasons = packet.FitReasonLines
            },
            Profile = profile is null ? null : new ProfileJson
            {
                TargetRoles = profile.TargetRoles,
                RemotePreference = profile.RemotePreference,
                PayFloor = profile.PayFloor,
                MustHaves = profile.MustHaves,
                DealBreakers = profile.DealBreakers,
                SkipCompanies = profile.SkipCompanies
            },
            StandingAnswers = profile is null || string.IsNullOrWhiteSpace(profile.StandingAnswers)
                ? null
                : profile.StandingAnswers.Trim(),
            ApplicationStatus = new ApplicationStatusJson
            {
                Status = application.Status.ToString(),
                DateSaved = application.DateSaved.ToString("yyyy-MM-dd"),
                DateApplied = application.DateApplied?.ToString("yyyy-MM-dd"),
                NextAction = application.NextAction,
                NextActionDue = application.NextActionDue?.ToString("yyyy-MM-dd"),
                Notes = string.IsNullOrWhiteSpace(application.Notes) ? null : application.Notes.Trim()
            },
            Resume = resume is null ? null : new ResumeJson
            {
                Name = resume.Name,
                FileName = resume.FileName,
                Notes = string.IsNullOrWhiteSpace(resume.Notes) ? null : resume.Notes,
                TextIncluded = packet.ResumeText.Text is not null,
                Truncated = packet.ResumeText.Truncated,
                Text = packet.ResumeText.Text,
                Note = packet.ResumeText.Note
            },
            AtsFormMap = packet.AtsFormMap,
            Cautions = packet.Cautions,
            ReadyPrompts = packet.ReadyPrompts
                .Select(p => new ReadyPromptJson { Title = p.Title, Prompt = p.Prompt })
                .ToList(),
            ReturnFormat = ReturnFormat
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private sealed class PacketJson
    {
        public int PacketVersion { get; set; }
        public string GeneratedAt { get; set; } = "";
        public int JobhuntApplicationId { get; set; }
        public string AtsDetected { get; set; } = "";
        public string? SourceUrl { get; set; }
        public IReadOnlyList<string> Instructions { get; set; } = Array.Empty<string>();
        public PostingJson Posting { get; set; } = new();
        public FitCheckJson FitCheck { get; set; } = new();
        public ProfileJson? Profile { get; set; }
        public string? StandingAnswers { get; set; }
        public ApplicationStatusJson ApplicationStatus { get; set; } = new();
        public ResumeJson? Resume { get; set; }
        public string AtsFormMap { get; set; } = "";
        public IReadOnlyList<string> Cautions { get; set; } = Array.Empty<string>();
        public List<ReadyPromptJson> ReadyPrompts { get; set; } = new();
        public IReadOnlyList<string> ReturnFormat { get; set; } = Array.Empty<string>();
    }

    private sealed class PostingJson
    {
        public string Company { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Url { get; set; }
        public string? Location { get; set; }
        public int? PayMin { get; set; }
        public int? PayMax { get; set; }
        public string? Text { get; set; }
    }

    private sealed class FitCheckJson
    {
        public string Verdict { get; set; } = "";
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
    }

    private sealed class ProfileJson
    {
        public string? TargetRoles { get; set; }
        public string? RemotePreference { get; set; }
        public int? PayFloor { get; set; }
        public string? MustHaves { get; set; }
        public string? DealBreakers { get; set; }
        public string? SkipCompanies { get; set; }
    }

    private sealed class ApplicationStatusJson
    {
        public string Status { get; set; } = "";
        public string DateSaved { get; set; } = "";
        public string? DateApplied { get; set; }
        public string? NextAction { get; set; }
        public string? NextActionDue { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class ResumeJson
    {
        public string Name { get; set; } = "";
        public string? FileName { get; set; }
        public string? Notes { get; set; }
        public bool TextIncluded { get; set; }
        public bool Truncated { get; set; }
        public string? Text { get; set; }
        public string? Note { get; set; }
    }

    private sealed class ReadyPromptJson
    {
        public string Title { get; set; } = "";
        public string Prompt { get; set; } = "";
    }

    // ---------- Shared pieces ----------

    private static IReadOnlyList<string> BuildCautions(
        JobApplication application, ResumeVersion? resume, ResumeTextResult resumeText, int otherApplicationsAtCompany)
    {
        var cautions = new List<string>();

        if (otherApplicationsAtCompany > 0)
        {
            cautions.Add(
                $"Duplicate-application warning: the user already has {otherApplicationsAtCompany} other " +
                $"application(s) tracked at {application.Company}. Many employers treat repeat applications " +
                "to the same role as spam — check before applying again.");
        }
        if (string.Equals(application.FitVerdict, FitCheckService.VerdictBlocked, StringComparison.OrdinalIgnoreCase))
        {
            cautions.Add("The fit check verdict is Blocked — surface the reasons in section 3 to the user before preparing anything.");
        }
        if (string.IsNullOrWhiteSpace(application.PostingText))
        {
            cautions.Add("No posting text was captured — ask the user for the full posting before drafting anything.");
        }
        if (resume is null)
        {
            cautions.Add("No resume version is attached to this packet — ask the user which resume to use.");
        }
        else if (resumeText.Text is null && resumeText.Note is not null)
        {
            cautions.Add("The chosen resume's text is not in this packet (see section 6) — the user must supply the actual PDF file.");
        }
        cautions.Add(
            "Voluntary self-identification (EEO) questions — gender, race/ethnicity, veteran or disability " +
            "status: answer only with a value the user set themselves, or choose \"Decline to self-identify\". " +
            "JobHunt HQ never infers demographic answers, and none are included in this packet.");
        cautions.Add(
            "Anything this packet doesn't cover — dates, metrics, credentials, availability — ask the user. " +
            "Never guess, and never sign or attest to anything on the user's behalf.");

        return cautions;
    }

    private static IReadOnlyList<(string Title, string Prompt)> BuildReadyPrompts(JobApplication application)
    {
        var role = application.RoleTitle;
        var company = application.Company;
        return new (string, string)[]
        {
            ("Prepare me",
             $"Using only the facts in this packet, prepare my application for {role} at {company}: summarise " +
             "the posting's must-haves, map each one to evidence from my resume and profile, and list anything " +
             "the packet doesn't cover that I need to decide."),
            ("Draft my answers",
             "Using only my standing answers and resume text in this packet, draft answers to this posting's " +
             "likely custom questions (for example \"Why this company?\"). Keep each answer under 150 words, in " +
             "my voice, and flag any question the packet doesn't cover instead of improvising."),
            ("Tailor my resume bullets",
             "Using only facts in this packet, suggest tailored resume bullet points for this posting. For " +
             "each suggestion, cite the source fact from my resume or profile it comes from. Do not add " +
             "skills, metrics, or employers that aren't in the packet."),
            ("Rehearse me",
             $"Using the posting in this packet, interview me for the {role} role at {company}: ask one " +
             "question at a time based on the posting's requirements, and after each answer give feedback " +
             "against what the posting asks for.")
        };
    }

    private static void AppendField(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.AppendLine($"- {label}: {value.Trim()}");
        }
    }

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
