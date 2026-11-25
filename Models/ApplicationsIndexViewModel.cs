namespace JobHuntHQ.Models;

/// <summary>Model for the applications list page.</summary>
public class ApplicationsIndexViewModel
{
    public List<JobApplication> Applications { get; set; } = new();

    public int TotalCount { get; set; }

    /// <summary>The status filter from the query string, if any.</summary>
    public ApplicationStatus? StatusFilter { get; set; }

    /// <summary>Free-text search query (matches company, role, location, next action, or notes).</summary>
    public string? SearchQuery { get; set; }

    /// <summary>Fit verdict filter (Strong fit, Possible, Poor fit, Blocked, Not checked).</summary>
    public string? FitFilter { get; set; }

    /// <summary>Follow-up filter: overdue or due-soon.</summary>
    public string? DueFilter { get; set; }

    /// <summary>Sort order: updated (default), due, applied, company.</summary>
    public string SortBy { get; set; } = "updated";

    public bool HasActiveFilters =>
        StatusFilter.HasValue
        || !string.IsNullOrWhiteSpace(SearchQuery)
        || !string.IsNullOrWhiteSpace(FitFilter)
        || !string.IsNullOrWhiteSpace(DueFilter)
        || !string.Equals(SortBy, "updated", StringComparison.OrdinalIgnoreCase);

    /// <summary>Today, for overdue / due-soon highlighting.</summary>
    public DateOnly Today { get; set; }

    /// <summary>"Due soon" window: due dates within this many days get highlighted.</summary>
    public int DueSoonDays { get; set; } = 7;
}
