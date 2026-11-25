namespace JobHuntHQ.Models;

/// <summary>One skipped CSV row: where it was and why it did not import.</summary>
public class ImportSkippedRow
{
    public int LineNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Outcome of a CSV import, shown on the result page.</summary>
public class ImportResultViewModel
{
    public int ImportedCount { get; set; }

    public int DuplicateCount { get; set; }

    public List<ImportSkippedRow> Skipped { get; set; } = new();

    /// <summary>Non-fatal notes (e.g. an unrecognized status that defaulted to Saved).</summary>
    public List<string> Warnings { get; set; } = new();
}
