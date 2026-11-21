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

    /// <summary>The generated bring-your-own-AI packet, as Markdown.</summary>
    public string PacketMarkdown { get; set; } = string.Empty;

    /// <summary>Today, for overdue highlighting.</summary>
    public DateOnly Today { get; set; }
}
