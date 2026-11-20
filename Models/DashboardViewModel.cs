namespace JobHuntHQ.Models;

/// <summary>Model for the signed-in home page dashboard (the user's data only).</summary>
public class DashboardViewModel
{
    /// <summary>Application counts per status (every status is present, zero-filled).</summary>
    public IReadOnlyDictionary<ApplicationStatus, int> StatusCounts { get; set; }
        = new Dictionary<ApplicationStatus, int>();

    public int TotalApplications { get; set; }

    /// <summary>Open applications whose next action is due between today and +7 days.</summary>
    public List<JobApplication> DueSoon { get; set; } = new();

    /// <summary>The 5 most recently updated applications.</summary>
    public List<JobApplication> RecentlyUpdated { get; set; } = new();
}
