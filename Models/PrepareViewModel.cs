namespace JobHuntHQ.Models;

/// <summary>One must-have keyword checked against a posting's text.</summary>
public sealed record MustHaveCheck(string Keyword, bool IsPresent);

/// <summary>
/// Model for the per-application "Prepare" page (M4): a read-only assembly of
/// the posting, the fit check, the user's profile material, resumes, and the
/// generated AI packet.
/// </summary>
public class PrepareViewModel
{
    public JobApplication Application { get; set; } = null!;

    public SearchProfile? Profile { get; set; }

    /// <summary>All of the user's resume versions, newest first.</summary>
    public List<ResumeVersion> Resumes { get; set; } = new();

    /// <summary>The resume the packet names: the application's chosen one, else the newest.</summary>
    public ResumeVersion? ChosenResume { get; set; }

    /// <summary>Must-have keywords from the profile, each marked present/missing in the posting text.</summary>
    public List<MustHaveCheck> MustHaveChecks { get; set; } = new();

    /// <summary>Instant deterministic Resume-vs-Posting skill match (no external AI required).</summary>
    public Services.ResumeMatchReport? ResumeMatch { get; set; }

    /// <summary>Parsed standing answers for one-click copying on application forms.</summary>
    public List<Services.StandingAnswerCard> StandingAnswerCards { get; set; } = new();

    /// <summary>The generated bring-your-own-AI packet, as Markdown.</summary>
    public string PacketMarkdown { get; set; } = string.Empty;

    /// <summary>Ready prompts from the packet, exposed as interactive copy cards.</summary>
    public IReadOnlyList<(string Title, string Prompt)> ReadyPrompts { get; set; }
        = Array.Empty<(string Title, string Prompt)>();

    /// <summary>ATS platform detected from the posting URL, for display (e.g. "Greenhouse", "Unknown").</summary>
    public string AtsDetectedName { get; set; } = "Unknown";

    /// <summary>The short self-contained starter prompt for the user's own AI (deep links + copy button).</summary>
    public string AiShortPrompt { get; set; } = string.Empty;

    /// <summary>Deep link opening a new ChatGPT chat with the short prompt. The packet itself is never in the URL.</summary>
    public string ChatGptUrl { get; set; } = string.Empty;

    /// <summary>Deep link opening a new Claude chat with the short prompt. The packet itself is never in the URL.</summary>
    public string ClaudeUrl { get; set; } = string.Empty;

    /// <summary>Today, for overdue highlighting.</summary>
    public DateOnly Today { get; set; }
}
