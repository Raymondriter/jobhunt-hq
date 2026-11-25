namespace JobHuntHQ.Models;

/// <summary>Model for the signed-in home page dashboard (the user's data only).</summary>
public class DashboardViewModel
{
    /// <summary>Application counts per status (every status is present, zero-filled).</summary>
    public IReadOnlyDictionary<ApplicationStatus, int> StatusCounts { get; set; }
        = new Dictionary<ApplicationStatus, int>();

    public int TotalApplications { get; set; }

    public int ActiveApplicationsCount { get; set; }

    public int AppliedCount { get; set; }

    public int RespondedCount { get; set; }

    public int? ResponseRatePercent =>
        AppliedCount > 0 ? (int)Math.Round(100.0 * RespondedCount / AppliedCount) : null;

    public bool HasSearchProfile { get; set; }

    public bool HasResumeVersion { get; set; }

    public bool HasUploadedResumePdf { get; set; }

    public bool SetupComplete => HasSearchProfile && HasResumeVersion && TotalApplications > 0;

    public DateOnly Today { get; set; }

    /// <summary>Open applications with an overdue next action or one due within seven days.</summary>
    public List<JobApplication> DueSoon { get; set; } = new();

    /// <summary>Scheduled, incomplete interview rounds ordered chronologically.</summary>
    public List<InterviewRound> UpcomingInterviews { get; set; } = new();

    /// <summary>Applications currently in Screening or Interview stage.</summary>
    public List<JobApplication> ActiveInterviewApplications { get; set; } = new();

    /// <summary>The 5 most recently updated applications.</summary>
    public List<JobApplication> RecentlyUpdated { get; set; } = new();
}
