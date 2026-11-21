namespace JobHuntHQ.Models;

/// <summary>One pipeline column on the Board page: a status and its applications.</summary>
public class BoardColumn
{
    public ApplicationStatus Status { get; set; }

    public List<JobApplication> Applications { get; set; } = new();
}

/// <summary>Model for the pipeline Board page (M4): applications grouped by status, in pipeline order.</summary>
public class BoardViewModel
{
    public List<BoardColumn> Columns { get; set; } = new();

    /// <summary>Today, for overdue highlighting on cards.</summary>
    public DateOnly Today { get; set; }
}
