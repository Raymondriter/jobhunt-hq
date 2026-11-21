namespace JobHuntHQ.Models;

/// <summary>One pipeline column on the Board page: a status and its applications.</summary>
public class BoardColumn
{
    public ApplicationStatus Status { get; set; }

    public List<JobApplication> Applications { get; set; } = new();

    /// <summary>
    /// Soft work-in-progress limit for this column (null = no nudge). Display
    /// only — going over it shows <see cref="WipHint"/> and never blocks a move.
    /// </summary>
    public int? WipSoftLimit { get; set; }

    /// <summary>
    /// The nudge text shown after the count when the column is over its soft
    /// limit, e.g. "consider a cleanup or a follow-up sweep". The view renders
    /// "{count} here — {WipHint}"; board.js recomputes the same string as
    /// cards move, from the column's data attributes.
    /// </summary>
    public string? WipHint { get; set; }
}

/// <summary>Model for the pipeline Board page (M4): applications grouped by status, in pipeline order.</summary>
public class BoardViewModel
{
    public List<BoardColumn> Columns { get; set; } = new();

    /// <summary>Today, for overdue highlighting on cards.</summary>
    public DateOnly Today { get; set; }
}
