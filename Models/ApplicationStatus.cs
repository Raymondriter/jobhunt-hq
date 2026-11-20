namespace JobHuntHQ.Models;

/// <summary>
/// The pipeline stages a job application moves through. The first five are the
/// happy path (Saved → Applied → Screening → Interview → Offer); the last three
/// are closed outcomes.
/// </summary>
public enum ApplicationStatus
{
    Saved,
    Applied,
    Screening,
    Interview,
    Offer,
    Rejected,
    Ghosted,
    Withdrawn
}

public static class ApplicationStatusExtensions
{
    /// <summary>Closed statuses no longer need follow-up actions.</summary>
    public static bool IsClosed(this ApplicationStatus status) =>
        status is ApplicationStatus.Rejected or ApplicationStatus.Ghosted or ApplicationStatus.Withdrawn;
}
