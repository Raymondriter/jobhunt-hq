namespace JobHuntHQ.Models;

/// <summary>Model for the applications list page.</summary>
public class ApplicationsIndexViewModel
{
    public List<JobApplication> Applications { get; set; } = new();

    /// <summary>The status filter from the query string, if any.</summary>
    public ApplicationStatus? StatusFilter { get; set; }

    /// <summary>Today, for overdue / due-soon highlighting.</summary>
    public DateOnly Today { get; set; }

    /// <summary>"Due soon" window: due dates within this many days get highlighted.</summary>
    public int DueSoonDays { get; set; } = 7;
}
