using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// One tracked job application, owned by exactly one user.
/// </summary>
public class JobApplication
{
    public int Id { get; set; }

    /// <summary>Owner (ASP.NET Core Identity user id). Set server-side, never bound from a form.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Role title")]
    public string RoleTitle { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Posting URL")]
    public string? Url { get; set; }

    [StringLength(200)]
    public string? Location { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Saved;

    [Display(Name = "Date saved")]
    public DateOnly DateSaved { get; set; }

    [Display(Name = "Date applied")]
    public DateOnly? DateApplied { get; set; }

    [StringLength(500)]
    [Display(Name = "Next action")]
    public string? NextAction { get; set; }

    [Display(Name = "Next action due")]
    public DateOnly? NextActionDue { get; set; }

    [Display(Name = "Salary min")]
    public int? SalaryMin { get; set; }

    [Display(Name = "Salary max")]
    public int? SalaryMax { get; set; }

    [Display(Name = "Resume used")]
    public int? ResumeVersionId { get; set; }

    public ResumeVersion? ResumeVersion { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    // ---- M4: posting capture + fit check ----

    /// <summary>URL of the captured posting (the "Add posting" flow). Kept alongside the older free-form <see cref="Url"/>.</summary>
    [StringLength(1000)]
    [Display(Name = "Posting URL")]
    public string? PostingUrl { get; set; }

    /// <summary>The full pasted posting text, used by the fit check and the AI packet. Long free text.</summary>
    [Display(Name = "Posting text")]
    public string? PostingText { get; set; }

    /// <summary>Low end of the pay range stated in the posting, if any.</summary>
    [Display(Name = "Posting pay min")]
    public int? PayMin { get; set; }

    /// <summary>High end of the pay range stated in the posting, if any.</summary>
    [Display(Name = "Posting pay max")]
    public int? PayMax { get; set; }

    /// <summary>Latest fit-check verdict: Blocked / Poor fit / Possible / Strong fit / Not checked.</summary>
    [StringLength(50)]
    [Display(Name = "Fit verdict")]
    public string? FitVerdict { get; set; }

    /// <summary>Latest fit-check reasons, one per line, in the user's own criteria's terms.</summary>
    [Display(Name = "Fit reasons")]
    public string? FitReasons { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
