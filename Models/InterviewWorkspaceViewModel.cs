using Microsoft.AspNetCore.Mvc.Rendering;

namespace JobHuntHQ.Models;

/// <summary>
/// ViewModel for <c>/Applications/Interview/{id}</c>: combines interview round
/// scheduling, elevator pitch &amp; research, matched STAR stories, the
/// self-contained Interview AI Packet, and the distraction-free Live Call
/// Cheat Sheet mode.
/// </summary>
public class InterviewWorkspaceViewModel
{
    public JobApplication Application { get; set; } = null!;

    public SearchProfile? Profile { get; set; }

    public List<ResumeVersion> Resumes { get; set; } = new();

    public ResumeVersion? ChosenResume { get; set; }

    public List<SelectListItem> ResumeOptions { get; set; } = new();

    public List<InterviewRound> Rounds { get; set; } = new();

    /// <summary>The active/upcoming round (or the latest round if all are completed).</summary>
    public InterviewRound? FocusRound { get; set; }

    /// <summary>Populated when editing an existing round inline.</summary>
    public InterviewRound? EditingRound { get; set; }

    /// <summary>Default round number suggested for the "Add round" form.</summary>
    public int NextRoundNumber { get; set; } = 1;

    /// <summary>All of the user's reusable STAR stories.</summary>
    public List<StoryBankItem> AllStories { get; set; } = new();

    /// <summary>Ids of STAR stories whose tags/title/actions match this posting's keywords or must-haves.</summary>
    public HashSet<int> MatchedStoryIds { get; set; } = new();

    public List<MustHaveCheck> MustHaveChecks { get; set; } = new();

    /// <summary>Instant deterministic Resume-vs-Posting skill match (no external AI required).</summary>
    public Services.ResumeMatchReport? ResumeMatch { get; set; }

    /// <summary>Self-contained Interview Prep AI Packet as Markdown.</summary>
    public string InterviewPacketMarkdown { get; set; } = string.Empty;

    /// <summary>Tailored prompts for mock interviewing, pitch sharpening, gap mapping, and thank-you notes.</summary>
    public IReadOnlyList<(string Title, string Prompt)> ReadyPrompts { get; set; }
        = Array.Empty<(string Title, string Prompt)>();

    public string AiShortPrompt { get; set; } = string.Empty;

    public string ChatGptUrl { get; set; } = string.Empty;

    public string ClaudeUrl { get; set; } = string.Empty;

    /// <summary>When true, renders the compact second-monitor / print-friendly Live Call Cheat Sheet HUD.</summary>
    public bool LiveMode { get; set; }

    public DateOnly Today { get; set; }
}
