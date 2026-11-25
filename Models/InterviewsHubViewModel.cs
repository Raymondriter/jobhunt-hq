namespace JobHuntHQ.Models;

/// <summary>
/// ViewModel for the top-level <c>/Interviews</c> Command Center: upcoming
/// scheduled rounds, active interview-stage applications, and the user's
/// reusable STAR Story Bank.
/// </summary>
public class InterviewsHubViewModel
{
    public List<InterviewRound> UpcomingRounds { get; set; } = new();

    public List<InterviewRound> CompletedRounds { get; set; } = new();

    /// <summary>Applications currently in Screening, Interview, or Offer.</summary>
    public List<JobApplication> ActiveInterviewApplications { get; set; } = new();

    /// <summary>The user's reusable STAR Story Bank.</summary>
    public List<StoryBankItem> Stories { get; set; } = new();

    /// <summary>Populated when editing a STAR story inline.</summary>
    public StoryBankItem? EditingStory { get; set; }

    public DateOnly Today { get; set; }
}
